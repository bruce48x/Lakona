"""Real-process shutdown regression, using only Python's standard library.

The HTTP driver issues normal generated Actor calls. File barriers control only
test Actor method/Stop completion; the host, membership, directory, and TCP stack
remain the production implementations. Never equate a killed process with a
successful graceful shutdown.
"""

import argparse
import concurrent.futures
import ctypes
import hashlib
import json
import os
from pathlib import Path
import shutil
import signal
import socket
import subprocess
import sys
import time
import traceback
import urllib.error
import urllib.request
import uuid


def require(condition, message):
    if not condition:
        raise AssertionError(message)


def eventually(check, description, timeout=30):
    deadline = time.monotonic() + timeout
    last_error = None
    while time.monotonic() < deadline:
        try:
            value = check()
            if value:
                return value
        except (urllib.error.URLError, TimeoutError, ConnectionError) as error:
            last_error = error
        time.sleep(0.1)
    raise TimeoutError(f"{description}; last error: {last_error}")


def free_port():
    with socket.socket() as sock:
        sock.bind(("127.0.0.1", 0))
        return sock.getsockname()[1]


def send_interrupt(pid):
    if os.name == "nt":
        # Attach from a short-lived helper, never detach the driver's console.
        subprocess.run([sys.executable, __file__, "--signal", str(pid)], check=True, timeout=10)
    else:
        os.kill(pid, signal.SIGINT)


def windows_signal(pid):
    kernel = ctypes.WinDLL("kernel32", use_last_error=True)
    kernel.FreeConsole()
    if not kernel.AttachConsole(pid):
        raise ctypes.WinError(ctypes.get_last_error())
    if not kernel.SetConsoleCtrlHandler(None, True):
        raise ctypes.WinError(ctypes.get_last_error())
    if not kernel.GenerateConsoleCtrlEvent(0, 0):
        raise ctypes.WinError(ctypes.get_last_error())
    time.sleep(0.2)
    kernel.FreeConsole()


class Node:
    def __init__(self, run, name, role, connection, suffix=""):
        self.name = name
        self.root = run.root / (name + suffix)
        self.root.mkdir()
        self.app = self.root / "app"
        shutil.copytree(run.app, self.app)
        hotfix = self.app / "hotfix"
        hotfix.mkdir()
        for path in run.hotfix.glob("Server.Hotfix.*"):
            shutil.copy2(path, hotfix / path.name)
        self.http = free_port()
        self.health = free_port()
        self.cluster = free_port()
        config = {
            "ConnectionStrings": {"ProbeMembership": connection},
            "Lakona": {
                "Node": {"Id": name, "Roles": [role]},
                "Cluster": {
                    "Endpoint": f"tcp://127.0.0.1:{self.cluster}",
                    "Membership": {
                        "Provider": "Postgres", "ConnectionStringName": "ProbeMembership",
                        "TableRefreshSeconds": 1, "IAmAliveSeconds": 10,
                        "AllowedIAmAliveMissSeconds": 60,
                        "ProbeIntervalSeconds": 1, "ProbeTimeoutSeconds": 2,
                    },
                },
                "Hotfix": {"DebugWatcher": "On"},
                "Actors": {"CallTimeoutSeconds": 60, "DeactivationTimeoutSeconds": 60},
                "Http": {"Listeners": [{
                    "Id": "probe", "Host": "127.0.0.1", "Port": self.http,
                    "Services": ["process-probe"], "RequestTimeoutSeconds": 65,
                }]},
                "Management": {"Http": {"Host": "127.0.0.1", "Port": self.health}},
                "Health": {"Enabled": True, "RequireLoopback": True},
            },
        }
        (self.app / "appsettings.json").write_text(json.dumps(config, indent=2), encoding="utf-8")
        env = {key: value for key, value in os.environ.items()
               if not key.lower().startswith(("lakona__", "connectionstrings__", "aspnetcore_", "dotnet_environment"))}
        env["PROBE_DIRECTORY"] = str(self.root)
        options = {}
        if os.name == "nt":
            startup = subprocess.STARTUPINFO()
            startup.dwFlags |= subprocess.STARTF_USESHOWWINDOW
            startup.wShowWindow = subprocess.SW_HIDE
            options = {"creationflags": subprocess.CREATE_NEW_CONSOLE, "startupinfo": startup}
        else:
            options = {"start_new_session": True}
        self.out = (self.root / "stdout.log").open("wb")
        self.err = (self.root / "stderr.log").open("wb")
        self.process = subprocess.Popen(
            ["dotnet", str(self.app / "Lakona.Game.ProcessTests.App.dll")],
            cwd=self.app, env=env, stdout=self.out, stderr=self.err, **options)
        self.interrupted = False
        run.nodes.append(self)
        eventually(self.ready, f"{name} Ready", 60)
        self.identity = self.request("status")
        run.result["processes"].append({
            "node": name, "pid": self.process.pid, "incarnation": self.identity["Incarnation"],
            "http": self.http, "health": self.health, "tcp": self.cluster,
        })
        print(f"Ready {name} pid={self.process.pid} incarnation={self.identity['Incarnation']}", flush=True)

    def alive(self):
        require(self.process.poll() is None,
                f"{self.name} exited unexpectedly: {self.process.returncode}; {self.root / 'stderr.log'}")

    def ready(self):
        self.alive()
        try:
            with urllib.request.urlopen(
                    f"http://127.0.0.1:{self.health}/_lakona/health/ready", timeout=2) as response:
                return json.load(response).get("succeeded", False)
        except urllib.error.HTTPError as error:
            if error.code == 503:
                return False
            raise

    def request(self, action, **body):
        self.alive()
        request = urllib.request.Request(
            f"http://127.0.0.1:{self.http}/probe/{action}", data=json.dumps(body).encode(),
            headers={"Content-Type": "application/json"}, method="POST")
        with urllib.request.urlopen(request, timeout=70) as response:
            return json.load(response)

    def events(self, kind=None):
        path = self.root / "events.jsonl"
        if not path.exists():
            return []
        # An append may be in progress while polling. Only consume complete lines.
        text = path.read_text(encoding="utf-8")
        lines = text.splitlines() if text.endswith("\n") else text.splitlines()[:-1]
        values = [json.loads(line) for line in lines]
        return [event for event in values if kind is None or event["Kind"] == kind]

    def release(self, name):
        (self.root / ("release-" + name)).touch()

    def interrupt(self):
        self.alive()
        send_interrupt(self.process.pid)
        self.interrupted = True

    def stopped(self):
        code = self.process.wait(timeout=60)
        self.out.flush()
        self.err.flush()
        require(code == 0, f"{self.name} exit code {code}")
        require(not (self.root / "stderr.log").read_text(encoding="utf-8").strip(),
                f"{self.name} wrote stderr")
        stdout = (self.root / "stdout.log").read_text(encoding="utf-8")
        require("fail:" not in stdout and "warn:" not in stdout,
                f"{self.name} logged a warning/error; see stdout.log")


class Run:
    def __init__(self, repo):
        self.repo = repo
        base = repo / "artifacts/shutdown-processes"
        self.root = base / (time.strftime("%Y%m%d-%H%M%S") + "-" + uuid.uuid4().hex[:6])
        self.root.mkdir(parents=True)
        self.app = base / "build/bin/Lakona.Game.ProcessTests.App/release"
        self.hotfix = base / "build/bin/Lakona.Game.ProcessTests.Hotfix/release"
        self.nodes = []
        self.container = "lakona-shutdown-process-" + uuid.uuid4().hex[:10]
        self.container_started = False
        self.result = {"passed": False, "processes": [], "handoffs": [], "traffic": [],
                       "signal": "CTRL_C_EVENT" if os.name == "nt" else "SIGINT"}
        self.result["server_dll_sha256"] = hashlib.sha256((self.app / "Lakona.Game.Server.dll").read_bytes()).hexdigest()

    def start_database(self):
        schema = self.repo / "src/Lakona.Game.Clustering.Postgres/database/postgresql/membership.sql"
        subprocess.run([
            "docker", "run", "-d", "--rm", "--name", self.container,
            "-p", "127.0.0.1::5432", "-e", "POSTGRES_HOST_AUTH_METHOD=trust",
            "-e", "POSTGRES_USER=lakona", "-e", "POSTGRES_DB=shutdown",
            "--mount", f"type=bind,source={schema},target=/docker-entrypoint-initdb.d/membership.sql,readonly",
            "postgres:18-alpine"], check=True, capture_output=True, text=True, timeout=60)
        self.container_started = True
        endpoint = subprocess.check_output(["docker", "port", self.container, "5432/tcp"], text=True).strip()
        port = int(endpoint.rsplit(":", 1)[1])
        eventually(lambda: subprocess.run([
            "docker", "exec", self.container, "psql", "-U", "lakona", "-d", "shutdown", "-tAc",
            "select count(*) from lakona_membership_member"], capture_output=True, timeout=5).returncode == 0,
            "PostgreSQL schema initialization", 45)
        return f"Host=127.0.0.1;Port={port};Database=shutdown;Username=lakona"

    def converge(self, nodes):
        expected = sorted(node.name for node in nodes)
        def converged():
            snapshots = [node.request("status") for node in nodes]
            return snapshots if all(sorted(item["Active"]) == expected for item in snapshots) else None
        return eventually(converged, "all process membership views converge")

    def lookup(self, nodes, expected):
        observed = [node.request("lookup") for node in nodes]
        require(all(item == expected for item in observed), f"Directory changed: {observed}, expected {expected}")
        return observed

    def traffic(self, callers, expected, label, count=96):
        requests = [f"{label}-{i}" for i in range(count)]
        with concurrent.futures.ThreadPoolExecutor(max_workers=12) as pool:
            futures = [pool.submit(callers[i % len(callers)].request, "call", requestId=request)
                       for i, request in enumerate(requests)]
            replies = [future.result() for future in futures]
        require({reply["RequestId"] for reply in replies} == set(requests), "Missing or repeated reply ids")
        require(all(all(reply[key] == expected[key] for key in ("Node", "Incarnation", "Activation"))
                    for reply in replies), "Traffic executed on a different activation")
        counts = sorted(reply["Count"] for reply in replies)
        require(counts == list(range(counts[0], counts[0] + count)), "Counter gap/duplicate under concurrency")
        self.result["traffic"].append({"label": label, "requests": count, "activation": expected,
                                       "first_count": counts[0], "last_count": counts[-1]})
        self.lookup(callers, expected)

    def handoff(self, gateway, old, survivor, initial, label, hold_call=False):
        (old.root / "hold-stop").touch()
        held = None
        pool = concurrent.futures.ThreadPoolExecutor(max_workers=1)
        try:
            if hold_call:
                held = pool.submit(gateway.request, "call", requestId=label + "-held", hold=True)
                eventually(lambda: any(event["Value"]["RequestId"] == label + "-held"
                                       for event in old.events("execute-enter")), "in-flight call entered")
            old.interrupt()
            if held:
                # Observe NotReady while the call is still blocked. Membership
                # must remain Active until admitted work has drained.
                eventually(lambda: not old.ready(), "old node closed readiness")
                snapshot = gateway.request("status")
                require(any(entry["Node"] == old.name and entry["Incarnation"] == initial["Incarnation"]
                            and entry["Status"] == "Active" for entry in snapshot["Table"]),
                        "Stopping was published before admitted work drained")
                require(not held.done(), "Held request was abandoned")
                require(not old.events("stop-enter"), "ActorStop ran before admitted work drained")
                # An attempted new call while old work drains must not execute.
                try:
                    gateway.request("call", requestId=label + "-rejected")
                except urllib.error.HTTPError as error:
                    rejection = json.load(error)
                    require(error.code == 503 and rejection.get("Error") == "ActorBackpressureException"
                            and "Distributed work admission is closed" in rejection.get("Message", ""),
                            f"Expected fenced target rejection, got {error.code}: {rejection}")
                    self.result["drain_rejection"] = {"http": error.code, "body": rejection}
                else:
                    raise AssertionError("Old node accepted new work after shutdown began")
                old.release("call")
                reply = held.result(timeout=15)
                require(reply["Activation"] == initial["Activation"], "Held call moved activation")
            eventually(lambda: old.events("stop-enter"), "old ActorStop barrier")
            self.converge([gateway, survivor])
            snapshot = gateway.request("status")
            require(any(entry["Node"] == old.name and entry["Incarnation"] == initial["Incarnation"]
                        and entry["Status"] == "Stopping" for entry in snapshot["Table"]),
                    "Old incarnation was not committed Stopping")
            # Concurrent Ensures from independent processes must agree on one winner.
            with concurrent.futures.ThreadPoolExecutor(max_workers=8) as creators:
                futures = [creators.submit(node.request, "ensure") for node in [gateway, survivor] * 8]
                placements = [future.result() for future in futures]
            replacement = placements[0]
            require(all(value == replacement for value in placements), "Concurrent placement produced multiple claims")
            require(replacement["Node"] == survivor.name and replacement["Activation"] != initial["Activation"],
                    "Replacement did not move to survivor")
            require(replacement["Incarnation"] == survivor.identity["Incarnation"], "Wrong owner incarnation")
            old.alive()
            require(not old.events("release-enter"), "Old cleanup was not delayed until after replacement")
            self.traffic([gateway, survivor], replacement, label + "-before-release")
            require(gateway.request("release", activation=initial["Activation"])["Released"] is False,
                    "Stale directory release removed replacement")
            before = self.lookup([gateway, survivor], replacement)
            old.release("stop")
            old.stopped()
            releases = old.events("release-exit")
            require(len(releases) == 1 and releases[0]["Value"]["Activation"] == initial["Activation"]
                    and releases[0]["Value"]["Released"] is False,
                    f"Real old catalog cleanup did not conditionally reject stale claim: {releases}")
            after = self.lookup([gateway, survivor], replacement)
            require(before == after, "Old shutdown erased or recreated the new claim")
            self.traffic([gateway, survivor], replacement, label + "-after-release")
            self.result["handoffs"].append({
                "label": label, "old": initial, "new": replacement,
                "concurrent_ensure_count": len(placements), "actual_old_release": releases[0],
                "before": before, "after": after, "exit_code": old.process.returncode,
            })
            print(f"PASS {label}: replacement survived old process release and exit", flush=True)
            return replacement
        finally:
            old.release("call")
            old.release("stop")
            pool.shutdown(wait=True)

    def audit(self):
        events = sorted([event for node in self.nodes for event in node.events()], key=lambda event: event["Ticks"])
        executing = {}
        completed = set()
        starts = []
        stops = []
        for event in events:
            kind, value = event["Kind"], event["Value"]
            if kind == "actor-start":
                starts.append(value["Activation"])
            elif kind == "stop-exit":
                stops.append(value["Activation"])
            elif kind == "execute-enter":
                request = value["RequestId"]
                require(not executing, f"Overlapping Actor execution across processes: {executing}, {value}")
                require(request not in completed, f"Request executed more than once: {request}")
                executing[request] = value["Activation"]
            elif kind == "execute-exit":
                request = value["RequestId"]
                require(executing.pop(request) == value["Activation"], "Execution switched activation")
                completed.add(request)
        require(not executing, "Unfinished Actor request")
        require(len(starts) == 3 and len(set(starts)) == 3, f"Unexpected extra activation(s): {starts}")
        require(sorted(starts) == sorted(stops), "ActorStop missing or duplicated")
        expected_count = sum(item["requests"] for item in self.result["traffic"]) + 1
        require(len(completed) == expected_count, f"Execution journal count {len(completed)} != {expected_count}")
        require(not any(request.endswith("-rejected") for request in completed), "Rejected request executed")
        self.result["execution_audit"] = {
            "unique_executed_requests": len(completed), "activations": starts,
            "overlapping_executions": 0, "duplicate_executions": 0,
            "actor_stop_count": len(stops),
        }
        (self.root / "merged-events.json").write_text(json.dumps(events, indent=2), encoding="utf-8")

    def execute(self):
        connection = self.start_database()
        gateway = Node(self, "gateway", "gateway", connection)
        first = Node(self, "worker-a", "worker", connection)
        second = Node(self, "worker-b", "worker", connection)
        self.converge([gateway, first, second])
        initial = gateway.request("ensure")
        require(initial["Node"] == first.name, "Initial placement must use worker-a")
        self.lookup([gateway, first, second], initial)
        self.traffic([gateway, second], initial, "initial")
        replacement = self.handoff(gateway, first, second, initial, "a-to-b", hold_call=True)
        restarted = Node(self, "worker-a", "worker", connection, "-restart")
        require(first.identity["Incarnation"] != restarted.identity["Incarnation"], "Restart reused incarnation")
        self.converge([gateway, second, restarted])
        require(restarted.request("ensure") == replacement, "Joining old NodeId stole live activation")
        self.lookup([gateway, second, restarted], replacement)
        final = self.handoff(gateway, second, restarted, replacement, "b-to-new-a")
        # Replay the original incarnation's release after the same NodeId now
        # owns a new activation. Stable node names must never authorize cleanup.
        same_node_release = gateway.request("release", activation=initial["Activation"])
        require(same_node_release["Released"] is False, "Old incarnation erased same-node replacement")
        self.lookup([gateway, restarted], final)
        self.result["same_node_old_incarnation_replay"] = {
            "old": initial, "new": final, "result": same_node_release,
        }
        gateway.interrupt()
        gateway.stopped()
        self.converge([restarted])
        self.lookup([restarted], final)
        self.traffic([restarted], final, "last-active", 32)
        restarted.interrupt()
        restarted.stopped()
        last_releases = restarted.events("release-exit")
        require(len(last_releases) == 1 and last_releases[0]["Value"]["Activation"] == final["Activation"]
                and last_releases[0]["Value"]["Released"] is True,
                "Last Active node did not finish its exact release on the empty ring")
        self.result["last_active_release"] = last_releases[0]
        self.audit()
        # All hosts stopped before the isolated database is touched.
        rows = subprocess.check_output([
            "docker", "exec", self.container, "psql", "-U", "lakona", "-d", "shutdown", "-tAc",
            "select coalesce(json_agg(json_build_object('node',node_id,'incarnation',node_incarnation,'status',status)), '[]'::json) from lakona_membership_member"
        ], text=True)
        self.result["final_membership"] = json.loads(rows)
        require(len(self.result["final_membership"]) == 4 and all(row["status"] == 3 for row in self.result["final_membership"]),
                "Not all process incarnations committed Dead")
        self.result["passed"] = True

    def cleanup(self):
        errors = []
        for node in reversed(self.nodes):
            node.release("call")
            node.release("stop")
        for node in reversed(self.nodes):
            try:
                if node.process.poll() is None:
                    if not node.interrupted:
                        node.interrupt()
                    node.process.wait(timeout=60)
            except Exception as error:
                errors.append(f"{node.name}: {error}")
            node.out.close()
            node.err.close()
        # Keep dependencies alive if a shutdown is stuck, and report it as a failure.
        if self.container_started and all(node.process.poll() is not None for node in self.nodes):
            logs = subprocess.check_output(["docker", "logs", self.container], stderr=subprocess.STDOUT)
            (self.root / "postgres.log").write_bytes(logs)
            subprocess.run(["docker", "stop", self.container], check=True, capture_output=True, timeout=30)
            self.result["database_removed_after_hosts_exited"] = True
        if errors:
            self.result["passed"] = False
            self.result["cleanup_errors"] = errors
        self.result["exit_codes"] = {node.root.name: node.process.poll() for node in self.nodes}
        (self.root / "result.json").write_text(json.dumps(self.result, indent=2), encoding="utf-8")
        print(f"Evidence: {self.root}", flush=True)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--repository", type=Path)
    parser.add_argument("--signal", type=int)
    args = parser.parse_args()
    if args.signal:
        windows_signal(args.signal)
        return 0
    run = Run(args.repository.resolve())
    try:
        run.execute()
    except Exception:
        run.result["error"] = traceback.format_exc()
        print(run.result["error"], file=sys.stderr, flush=True)
    finally:
        run.cleanup()
    print(json.dumps(run.result, indent=2), flush=True)
    return 0 if run.result["passed"] else 1


if __name__ == "__main__":
    sys.exit(main())
