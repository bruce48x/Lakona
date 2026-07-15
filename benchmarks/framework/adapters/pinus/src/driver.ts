import { EventEmitter } from "events";
import { createHash } from "crypto";
import { readFileSync, writeFileSync } from "fs";
import { resolve } from "path";

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
  if (command.workload !== "frontdoor.echo" && command.workload !== "cluster.direct") {
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

    const accumulator = new Accumulator(command);
    const measurementEnd = process.hrtime.bigint() +
      (BigInt(command.timing.measurementMilliseconds) * 1_000_000n);
    await Promise.all(clients.map(client => measure(client, command, ids, accumulator, measurementEnd)));
    writeFileSync(resultPath, `${JSON.stringify(accumulator.result(command), null, 2)}\n`);
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
      { requestId, payload },
      command.timing.requestTimeoutMilliseconds,
      () => undefined,
      routeFor(command.workload));
    if (!valid(response, requestId, payload, terminalNodeFor(command.workload))) {
      throw new Error("Pinus returned an invalid response during warm-up.");
    }
  }
}

async function measure(
  client: PomeloClient,
  command: CaseCommand,
  ids: { value: number },
  accumulator: Accumulator,
  end: bigint): Promise<void> {
  while (process.hrtime.bigint() < end) {
    const requestId = ++ids.value;
    const payload = createPayload(command.seed, requestId, command.payloadSize);
    accumulator.started++;
    const started = process.hrtime.bigint();
    try {
      const response = await request(
        client,
        { requestId, payload },
        command.timing.requestTimeoutMilliseconds,
        () => accumulator.duplicateResponses++,
        routeFor(command.workload));
      accumulator.recordLatency(Number((process.hrtime.bigint() - started + 999n) / 1_000n));
      accumulator.completed++;
      switch (classifyResponse(response, requestId, payload, terminalNodeFor(command.workload))) {
        case "rejected": accumulator.rejected++; break;
        case "corrupt": accumulator.corrupt++; break;
        case "misrouted": accumulator.misrouted++; break;
        case "succeeded": accumulator.succeeded++; break;
      }
    } catch (error) {
      if (error instanceof TimeoutError) {
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
  route = "connector.echoHandler.echo"): Promise<any> {
  return new Promise((accept, reject) => {
    let settled = false;
    const onDisconnect = () => finish(() => reject(new DisconnectError("Pinus client disconnected.")));
    const timer = setTimeout(() => finish(() => reject(new TimeoutError("Pinus request timed out."))), timeoutMilliseconds);
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
    client.once("disconnect", onDisconnect);
    client.request(route, message, response => {
      if (!finish(() => accept(response))) {
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
  return workload === "cluster.direct" ? "connector.echoHandler.direct" : "connector.echoHandler.echo";
}

function terminalNodeFor(workload: string): string {
  return workload === "cluster.direct" ? "worker-server-1" : "connector-server-1";
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
        canceledAtDrain: 0,
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
