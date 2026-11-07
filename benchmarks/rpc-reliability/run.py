"""Local, two-process RPC A/B experiment. Python standard library only."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import queue
import shutil
import subprocess
import threading
import time
import zipfile

ROOT = Path(__file__).resolve().parents[2]
PACKAGES = ["Core", "Analyzers", "Client", "Server", "Transport.Kcp", "Transport.Tcp", "Serializer.MemoryPack"]


def git(*args):
    return subprocess.check_output(["git", *args], cwd=ROOT)


def prepare(output, revision):
    paths = ["Directory.Build.props"] + [f"src/Lakona.Rpc.{p}" for p in PACKAGES]
    archive = output / "baseline.zip"
    subprocess.run(["git", "archive", "--format=zip", f"--output={archive}", revision, *paths], cwd=ROOT, check=True)
    with zipfile.ZipFile(archive) as source:
        source.extractall(output / "baseline")
    files = git("ls-files", "--cached", "--others", "--exclude-standard", "--", *paths).decode().splitlines()
    hashes = {}
    for name in sorted(set(files)):
        source = ROOT / name
        if not source.is_file():
            continue
        destination = output / "current" / name
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(source, destination)
        hashes[name] = hashlib.sha256(source.read_bytes()).hexdigest()
    (output / "current-sha256.json").write_text(json.dumps(hashes, indent=2))
    (output / "changes.patch").write_bytes(git("diff", revision, "--", *paths))
    (output / "manifest.json").write_text(json.dumps({
        "baseline": git("rev-parse", revision).decode().strip(), "created": time.strftime("%Y-%m-%dT%H:%M:%S%z"),
        "command": subprocess.list2cmdline(["python", *os.sys.argv]), "sdk": subprocess.check_output(["dotnet", "--version"]).decode().strip(),
        "policy": "Release; separate processes; workstation GC; tiering defaults; loopback; MemoryPack; default transport parameters; keepalive disabled",
    }, indent=2))
    for version in ("baseline", "current"):
        harness = output / version / "benchmarks" / "rpc-reliability"
        harness.mkdir(parents=True, exist_ok=True)
        for name in ("Program.cs", "WireTrace.cs", "IdleProbe.cs", "RpcReliability.csproj", "run.py", "report.py", "udp_relay.py", "README.md"):
            shutil.copy2(ROOT / "benchmarks/rpc-reliability" / name, harness / name)
        command = ["dotnet", "build", str(harness / "RpcReliability.csproj"), "-c", "Release", "--verbosity", "quiet", "-p:NuGetAudit=false"]
        with (output / f"build-{version}.log").open("w") as log:
            subprocess.run(command, cwd=ROOT, stdout=log, stderr=subprocess.STDOUT, check=True)
        print(f"BUILT {version}", flush=True)


class Child:
    def __init__(self, dll, args, log):
        self.log = log.open("w")
        self.stdout_log = Path(str(log) + ".stdout").open("w")
        self.process = subprocess.Popen(["dotnet", str(dll), *map(str, args)], stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                                        stderr=self.log, text=True, creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
        self.lines = queue.Queue()
        self.reader = threading.Thread(target=self.read, daemon=True)
        self.reader.start()

    def read(self):
        for line in self.process.stdout:
            self.stdout_log.write(line)
            self.stdout_log.flush()
            self.lines.put(line)
        self.lines.put(None)

    def receive(self, phase, timeout=90):
        end = time.monotonic() + timeout
        while True:
            try:
                line = self.lines.get(timeout=max(.001, end - time.monotonic()))
            except queue.Empty as error:
                raise TimeoutError(f"Process {self.process.pid} did not reach {phase} within {timeout}s") from error
            if line is None:
                raise RuntimeError(f"Process {self.process.pid} ended before {phase}; inspect stderr log")
            try:
                result = json.loads(line)
            except json.JSONDecodeError:
                continue  # Native diagnostics may also write stdout; retain them in the transcript.
            if result["phase"] != phase:
                raise RuntimeError(result)
            return result

    def send(self, text):
        self.process.stdin.write(text + "\n")
        self.process.stdin.flush()

    def close(self):
        if self.process.poll() is None:
            self.process.kill()
        self.process.wait(timeout=15)
        self.reader.join(timeout=5)
        self.stdout_log.close()
        self.log.close()


def run_case(output, version, transport, size, concurrency, repeat, mode, seconds, warmup, binary_root=None, on_failure=None, relay_factory=None):
    name = f"{mode}-{transport}-{size}-c{concurrency}-r{repeat}-{version}"
    result_file = output / f"{name}.json"
    failure_file = output / f"{name}.failure.json"
    if result_file.exists() or failure_file.exists():
        return
    dll = (binary_root or output) / version / "benchmarks/rpc-reliability/bin/Release/net10.0/Lakona.Rpc.Tests.dll"
    print(f"RUN {name}", flush=True)
    server = Child(dll, ["server", transport, size], output / f"{name}-server.log")
    client = None
    relay = None
    try:
        ready = server.receive("ready")
        if relay_factory is not None:
            relay = relay_factory(ready["port"])
        client = Child(dll, ["client", transport, size, relay.port if relay else ready["port"], mode, concurrency, seconds, warmup], output / f"{name}-client.log")
        client.receive("warm")
        server.send("begin")
        server.receive("measuring")
        client.send("go")
        result = client.receive("result", timeout=max(90, seconds + 30) if mode == "small" else 620)
        server.send("end")
        result["serverResources"] = server.receive("resources")["metrics"]
        result.update(version=version, repeat=repeat, environment=ready["environment"])
        if relay is not None:
            if relay.error is not None:
                raise RuntimeError(f"Relay failed: {relay.error}")
            result["relayStats"] = relay.stats
        server.send("stop")
        if client.process.wait(timeout=20) != 0 or server.process.wait(timeout=20) != 0:
            raise RuntimeError("Nonzero exit after measurement")
        result_file.write_text(json.dumps(result, indent=2))
        print(f"DONE {name}: {result['count']} ops, {result['elapsed']:.3f}s, {result['rps']:.1f}/s, p99 {result['p99Us']/1000:.3f}ms", flush=True)
    except Exception as error:
        if on_failure is not None:
            try:
                on_failure(name, server.process, client.process if client else None)
            except Exception as diagnostic_error:
                print(f"DIAGNOSTIC FAILED {name}: {diagnostic_error}", flush=True)
        failure_file.write_text(json.dumps(dict(case=name, version=version, transport=transport,
            size=size, concurrency=concurrency, repeat=repeat, mode=mode, error=repr(error)), indent=2))
        print(f"FAIL {name}: {error}", flush=True)
    finally:
        if client:
            client.close()
        server.close()
        if relay:
            relay.close()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", default="artifacts/rpc-reliability/2026-11-07")
    parser.add_argument("--baseline", default="HEAD")
    parser.add_argument("--skip-build", action="store_true")
    parser.add_argument("--suite", choices=["pilot", "small", "large", "all", "tcp-stress"], default="all")
    parser.add_argument("--repeats", type=int, default=3)
    parser.add_argument("--seconds", type=float, default=15)
    parser.add_argument("--warmup", type=float, default=3)
    args = parser.parse_args()
    output = (ROOT / args.output).resolve()
    output.mkdir(parents=True, exist_ok=True)
    if not args.skip_build:
        prepare(output, args.baseline)
        manifest_path = output / "manifest.json"
        manifest = json.loads(manifest_path.read_text())
        manifest["measurement"] = {"seconds": args.seconds, "warmupSeconds": args.warmup, "repeats": args.repeats, "suite": args.suite}
        manifest_path.write_text(json.dumps(manifest, indent=2))
    if args.suite == "pilot":
        for version in ("baseline", "current"):
            for transport in ("kcp", "tcp"):
                run_case(output, version, transport, 256, 1, 0, "small", 3, 1)
    if args.suite == "tcp-stress":
        for repeat in range(1, args.repeats + 1):
            for concurrency in (32, 128):
                versions = ("baseline", "current") if repeat % 2 else ("current", "baseline")
                for version in versions:
                    run_case(output, version, "tcp", 1024, concurrency, repeat, "small", args.seconds, args.warmup)
    if args.suite in ("small", "all"):
        for repeat in range(1, args.repeats + 1):
            for transport in ("kcp", "tcp"):
                for size, concurrency in ((256, 1), (256, 32), (1024, 1), (1024, 32)):
                    versions = ("baseline", "current") if repeat % 2 else ("current", "baseline")
                    for version in versions:
                        run_case(output, version, transport, size, concurrency, repeat, "small", args.seconds, args.warmup)
    if args.suite in ("large", "all"):
        for repeat in range(1, args.repeats + 1):
            for transport in ("kcp", "tcp"):
                for mode in ("upload", "download"):
                    run_case(output, "current", transport, 12 * 1024 * 1024, 1, repeat, mode, 0, args.warmup)
        for mode in ("upload", "download"):
            run_case(output, "current", "kcp", 24 * 1024 * 1024, 1, 1, mode, 0, args.warmup)
    if list(output.glob("*.failure.json")):
        raise SystemExit("Some cases failed; preserved failure records are not valid throughput samples.")


if __name__ == "__main__":
    main()
