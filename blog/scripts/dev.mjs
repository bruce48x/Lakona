import { spawn } from "node:child_process";
import hugo from "hugo-bin";
const forwarded = process.argv.slice(2).map((argument) => {
  if (argument === "--host") return "--bind";
  if (argument.startsWith("--host=")) return `--bind=${argument.slice(7)}`;
  return argument;
});

const child = spawn(
  hugo,
  ["server", "--buildDrafts", "--disableFastRender", ...forwarded],
  { stdio: "inherit" },
);

for (const signal of ["SIGINT", "SIGTERM"]) {
  process.on(signal, () => child.kill(signal));
}

child.on("error", (error) => {
  console.error(error);
  process.exitCode = 1;
});

child.on("exit", (code, signal) => {
  if (signal) {
    process.kill(process.pid, signal);
  } else {
    process.exitCode = code ?? 1;
  }
});
