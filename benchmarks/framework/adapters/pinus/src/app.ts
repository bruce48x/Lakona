import { mkdirSync, writeFileSync } from "fs";
import { join } from "path";
import "reflect-metadata";
import { workerNode } from "./routing";

const { pinus } = require("pinus") as { pinus: any };

console.log = (...values: unknown[]) => console.error(...values);

const options = readOptions(process.argv.slice(2));
const role = options.role ?? "frontdoor";
const clientPort = readPort(options, "clientPort");
const connectorPort = readPort(options, "connectorPort");
const worker1Port = readPort(options, "worker1Port");
const worker2Port = readPort(options, "worker2Port");
const masterPort = readPort(options, "masterPort");
const base = __dirname;
const configDirectory = join(base, "config");
mkdirSync(configDirectory, { recursive: true });
writeFileSync(join(configDirectory, "master.json"), JSON.stringify({
  development: { id: "master-server-1", host: "127.0.0.1", port: masterPort },
  production: { id: "master-server-1", host: "127.0.0.1", port: masterPort }
}));
const servers = {
  connector: [{
    id: "connector-server-1",
    host: "127.0.0.1",
    port: connectorPort,
    clientHost: "127.0.0.1",
    clientPort,
    frontend: true
  }],
  worker: [{
    id: "worker-server-1",
    host: "127.0.0.1",
    port: worker1Port
  }, {
    id: "worker-server-2",
    host: "127.0.0.1",
    port: worker2Port
  }]
};
writeFileSync(join(configDirectory, "servers.json"), JSON.stringify({
  development: servers,
  production: servers
}));

const app = pinus.createApp({ base });
app.set("name", "framework-benchmark-pinus");
app.route("worker", (routeFrom: unknown, _message: unknown, _app: unknown, callback: (error: Error | null, serverId?: string) => void) => {
  if (typeof routeFrom !== "string") {
    callback(new Error("A target key is required for worker routing."));
    return;
  }

  callback(null, workerNode(routeFrom));
});
app.configure("production|development", "connector", () => {
  app.set("connectorConfig", {
    connector: pinus.connectors.hybridconnector,
    heartbeat: 3,
    useDict: false,
    useProtobuf: false
  });
});

const startupGuard = setTimeout(() => {
  console.error("Pinus connector startup timed out.");
  process.exit(2);
}, 15_000);
app.start((error?: Error) => {
  clearTimeout(startupGuard);
  if (error) {
    console.error(error.stack ?? error.message);
    process.exitCode = 2;
    return;
  }

  process.stdout.write(`${JSON.stringify({
    event: "ready",
    role,
    nodeId: role === "master" ? "master-1" : role.startsWith("worker-") ? app.getServerId() : "connector-server-1",
    endpoints: role === "frontdoor" ? { client: `ws://127.0.0.1:${clientPort}` } : {}
  })}\n`);
});

function readOptions(args: string[]): Record<string, string> {
  return Object.fromEntries(args.map(value => {
    const separator = value.indexOf("=");
    return separator > 0 ? [value.slice(0, separator), value.slice(separator + 1)] : [value, ""];
  }));
}

function readPort(values: Record<string, string>, name: string): number {
  const value = Number(values[name]);
  if (!Number.isInteger(value) || value <= 0 || value > 65535) {
    throw new Error(`${name}=<port> is required.`);
  }

  return value;
}
