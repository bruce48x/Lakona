import { EventEmitter } from "events";
import { createHash } from "crypto";
import { readFileSync, writeFileSync } from "fs";
import { resolve } from "path";
import { targetKey, workerNode } from "./routing";

type PomeloClient = EventEmitter & {
  init(options: { host: string; port: number; user: object; handshakeCallback: () => void }, callback: () => void): void;
  request(route: string, message: object, callback: (response: any) => void): void;
  disconnect(): void;
};

interface CaseCommand {
  schemaVersion: string;
  caseId: string;
  framework: string;
  workload: string;
  payloadSize: number;
  concurrency: number;
  connectionCount: number;
  seed: number;
  timing: {
    warmupMilliseconds: number;
    measurementMilliseconds: number;
    requestTimeoutMilliseconds: number;
    drainTimeoutMilliseconds: number;
  };
  histogram: {
    unit: string;
    lowestDiscernibleValue: number;
    highestTrackableValue: number;
    significantDigits: number;
  };
  endpoints: Record<string, string>;
}

class TimeoutError extends Error {}
class DisconnectError extends Error {}
class DrainCanceledError extends Error {}

if (require.main === module) {
  main().catch(error => {
    console.error(error instanceof Error ? error.stack : String(error));
    process.exitCode = 2;
  });
}

async function main(): Promise<void> {
  const casePath = readOption("--case");
  const resultPath = readOption("--result");
  const command = JSON.parse(readFileSync(casePath, "utf8")) as CaseCommand;
  if (!(["frontdoor.echo", "cluster.direct", "cluster.routed"] as string[]).includes(command.workload)) {
    throw new Error(`Pinus driver does not support '${command.workload}'.`);
  }

  const endpoint = new URL(command.endpoints.client);
  const clients = await Promise.all(Array.from(
    { length: command.connectionCount },
    () => connect(endpoint)));
  try {
    const ids = { value: 0 };
    if (command.timing.warmupMilliseconds > 0) {
      const warmupEnd = Date.now() + command.timing.warmupMilliseconds;
      await Promise.all(clients.map(client => warmup(client, command, ids, warmupEnd)));
    }

    const result = await measureClients(clients, command, ids);
    writeFileSync(resultPath, `${JSON.stringify(result, null, 2)}\n`);
  } finally {
    for (const client of clients) {
      client.disconnect();
    }
  }
}

async function connect(endpoint: URL): Promise<PomeloClient> {
  const root: any = globalThis;
  root.window = root;
  const protocol = require("pinus-protocol");
  root.Protocol = {
    ...protocol.Protocol,
    Package: protocol.Package,
    Message: protocol.Message
  };
  root.protobuf = null;
  root.decodeIO_protobuf = null;
  root.EventEmitter = EventEmitter;
  root.rsa = null;
  root.localStorage = root.localStorage ?? {
    getItem: (_key: string) => null,
    setItem: (_key: string, _value: string) => undefined
  };

  const modulePath = require.resolve("pomelo-jsclient-websocket");
  delete require.cache[modulePath];
  const client = require(modulePath) as PomeloClient;
  await new Promise<void>((accept, reject) => {
    const onError = (error: unknown) => reject(error instanceof Error ? error : new Error(String(error)));
    client.once("io-error", onError);
    client.init({
      host: endpoint.hostname,
      port: Number(endpoint.port),
      user: {},
      handshakeCallback: () => undefined
    }, () => {
      client.removeListener("io-error", onError);
      accept();
    });
  });
  return client;
}

async function warmup(
  client: PomeloClient,
  command: CaseCommand,
  ids: { value: number },
  end: number): Promise<void> {
  while (Date.now() < end) {
    const requestId = ++ids.value;
    const payload = createPayload(command.seed, requestId, command.payloadSize);
    const response = await request(
      client,
      { requestId, payload, targetKey: targetKey(requestId) },
      command.timing.requestTimeoutMilliseconds,
      () => undefined,
      routeFor(command.workload));
    if (!valid(response, requestId, payload, terminalNodeFor(command.workload, requestId))) {
      throw new Error("Pinus returned an invalid response during warm-up.");
    }
  }
}

export async function measureClients(clients: PomeloClient[], command: CaseCommand, ids = { value: 0 }): Promise<object> {
  const accumulator = new Accumulator(command);
  const end = process.hrtime.bigint() + BigInt(command.timing.measurementMilliseconds) * 1_000_000n;
  const drainEnd = end + BigInt(command.timing.drainTimeoutMilliseconds) * 1_000_000n;
  await Promise.all(clients.map(client => measure(client, command, ids, accumulator, end, drainEnd)));
  return accumulator.result(command);
}

async function measure(
  client: PomeloClient,
  command: CaseCommand,
  ids: { value: number },
  accumulator: Accumulator,
  end: bigint,
  drainEnd: bigint): Promise<void> {
  while (process.hrtime.bigint() < end) {
    const requestId = ++ids.value;
    const payload = createPayload(command.seed, requestId, command.payloadSize);
    accumulator.started++;
    const started = process.hrtime.bigint();
    try {
      const response = await request(
        client,
        { requestId, payload, targetKey: targetKey(requestId) },
        command.timing.requestTimeoutMilliseconds,
        () => accumulator.duplicateResponses++,
        routeFor(command.workload),
        drainEnd);
      accumulator.recordLatency(Number((process.hrtime.bigint() - started + 999n) / 1_000n));
      accumulator.completed++;
      switch (classifyResponse(response, requestId, payload, terminalNodeFor(command.workload, requestId))) {
        case "rejected": accumulator.rejected++; break;
        case "corrupt": accumulator.corrupt++; break;
        case "misrouted": accumulator.misrouted++; break;
        case "succeeded": accumulator.succeeded++; break;
      }
    } catch (error) {
      if (error instanceof DrainCanceledError) {
        accumulator.canceledAtDrain++;
      } else if (error instanceof TimeoutError) {
        accumulator.timedOut++;
      } else {
        accumulator.disconnected++;
      }
      break;
    }
  }
}

export function request(
  client: PomeloClient,
  message: object,
  timeoutMilliseconds: number,
  onLateResponse: () => void = () => undefined,
  route = "connector.echoHandler.echo",
  drainEnd?: bigint): Promise<any> {
  return new Promise((accept, reject) => {
    let settled = false;
    let expiredAtDrain = false;
    const onDisconnect = () => finish(() => reject(new DisconnectError("Pinus client disconnected.")));
    const now = process.hrtime.bigint();
    const requestEnd = now + BigInt(timeoutMilliseconds) * 1_000_000n;
    const canceledAtDrain = drainEnd !== undefined && drainEnd <= requestEnd;
    const deadline = canceledAtDrain ? drainEnd! : requestEnd;
    const expire = () => finish(() => {
      expiredAtDrain = canceledAtDrain;
      reject(canceledAtDrain
        ? new DrainCanceledError("Pinus drain deadline reached.")
        : new TimeoutError("Pinus request timed out."));
    });
    const timer = setTimeout(expire, Math.max(0, Number(deadline - now) / 1_000_000));
    const finish = (action: () => void) => {
      if (settled) {
        return false;
      }
      settled = true;
      clearTimeout(timer);
      client.removeListener("disconnect", onDisconnect);
      action();
      return true;
    };
    if (deadline <= now) {
      expire();
      return;
    }
    client.once("disconnect", onDisconnect);
    client.request(route, message, response => {
      if (!finish(() => accept(response)) && !expiredAtDrain) {
        onLateResponse();
      }
    });
  });
}

function valid(response: any, requestId: number, payload: number[], terminalNode: string): boolean {
  return classifyResponse(response, requestId, payload, terminalNode) === "succeeded";
}

export function classifyResponse(
  response: any,
  requestId: number,
  payload: number[],
  terminalNode = "connector-server-1"): "succeeded" | "rejected" | "corrupt" | "misrouted" {
  if (response?.code && response.code !== 200) return "rejected";
  if (response?.requestId !== requestId || !equalPayload(response?.payload, payload)) return "corrupt";
  if (response?.terminalNode !== terminalNode) return "misrouted";
  return "succeeded";
}

function routeFor(workload: string): string {
  if (workload === "cluster.direct") return "connector.echoHandler.direct";
  if (workload === "cluster.routed") return "connector.echoHandler.routed";
  return "connector.echoHandler.echo";
}

function terminalNodeFor(workload: string, requestId: number): string {
  if (workload === "cluster.direct") return "worker-server-1";
  if (workload === "cluster.routed") return workerNode(targetKey(requestId));
  return "connector-server-1";
}

function equalPayload(actual: unknown, expected: number[]): boolean {
  return Array.isArray(actual) && actual.length === expected.length &&
    actual.every((value, index) => value === expected[index]);
}

export function createPayload(seed: number, requestId: number, size: number): number[] {
  return Array.from({ length: size }, (_, index) =>
    ((seed * 31) + (requestId * 17) + (index * 13)) & 0xff);
}

function readOption(name: string): string {
  const index = process.argv.indexOf(name);
  if (index < 0 || index + 1 >= process.argv.length) {
    throw new Error(`${name} <path> is required.`);
  }
  return resolve(process.argv[index + 1]);
}

class Accumulator {
  public started = 0;
  public completed = 0;
  public succeeded = 0;
  public rejected = 0;
  public corrupt = 0;
  public misrouted = 0;
  public timedOut = 0;
  public disconnected = 0;
  public canceledAtDrain = 0;
  public duplicateResponses = 0;
  private maximum = 0;
  private readonly buckets = new Map<number, number>();

  public constructor(private readonly command: CaseCommand) {}

  public recordLatency(microseconds: number): void {
    const value = Math.max(
      this.command.histogram.lowestDiscernibleValue,
      Math.min(this.command.histogram.highestTrackableValue, microseconds));
    this.maximum = Math.max(this.maximum, value);
    const upperBound = quantizeUpperBound(value, this.command.histogram.significantDigits);
    this.buckets.set(upperBound, (this.buckets.get(upperBound) ?? 0) + 1);
  }

  public result(command: CaseCommand): object {
    return {
      schemaVersion: "1",
      caseId: command.caseId,
      framework: command.framework,
      workload: command.workload,
      achievedRequestsPerSecond: this.succeeded / (command.timing.measurementMilliseconds / 1000),
      outcomes: {
        started: this.started,
        completed: this.completed,
        succeeded: this.succeeded,
        rejected: this.rejected,
        corrupt: this.corrupt,
        misrouted: this.misrouted,
        timedOut: this.timedOut,
        disconnected: this.disconnected,
        canceledAtDrain: this.canceledAtDrain,
        duplicateResponses: this.duplicateResponses
      },
      histogram: {
        unit: command.histogram.unit,
        lowestDiscernibleValue: command.histogram.lowestDiscernibleValue,
        highestTrackableValue: command.histogram.highestTrackableValue,
        significantDigits: command.histogram.significantDigits,
        totalCount: this.completed,
        maximum: this.maximum,
        buckets: [...this.buckets.entries()]
          .sort(([left], [right]) => left - right)
          .map(([upperBound, count]) => ({ upperBound, count }))
      },
      metadata: {
        runtime: process.version,
        transport: "Pinus hybridconnector WebSocket",
        serializer: "Pinus JSON (protobuf disabled)",
        clientLibrary: "pomelo-jsclient-websocket 0.1.1",
        lockfileSha256: createHash("sha256")
          .update(readFileSync(resolve(__dirname, "../package-lock.json")))
          .digest("hex"),
        connectionPolicy: "one persistent connection per outstanding slot"
      }
    };
  }
}

export function quantizeUpperBound(value: number, significantDigits: number): number {
  let scale = 1;
  const threshold = 10 ** significantDigits;
  let reduced = value;
  while (reduced >= threshold) {
    reduced = Math.floor(reduced / 10);
    scale *= 10;
  }
  return Math.ceil(value / scale) * scale;
}
