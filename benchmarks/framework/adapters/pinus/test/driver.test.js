const assert = require("node:assert/strict");
const { EventEmitter } = require("node:events");
const test = require("node:test");
const {
  classifyResponse,
  createPayload,
  quantizeUpperBound,
  measureClients,
  request
} = require("../dist/driver");
const { ownerNumber, targetKey, workerNode } = require("../dist/routing");

test("payload generation is deterministic at both version 1 sizes", () => {
  for (const size of [32, 256]) {
    const first = createPayload(20260715, 42, size);
    const second = createPayload(20260715, 42, size);
    assert.equal(first.length, size);
    assert.deepEqual(first, second);
  }
});

test("routed targets have stable ownership across both workers", () => {
  assert.equal(targetKey(256), "entity/0");
  assert.equal(ownerNumber("entity/0"), 2);
  assert.equal(workerNode("entity/1"), "worker-server-1");
  assert.deepEqual(new Set(Array.from({ length: 256 }, (_, index) => ownerNumber(targetKey(index)))), new Set([1, 2]));
});

test("response classification distinguishes every completed outcome", () => {
  const payload = createPayload(7, 9, 32);
  const valid = { requestId: 9, payload, terminalNode: "connector-server-1" };
  assert.equal(classifyResponse(valid, 9, payload), "succeeded");
  assert.equal(classifyResponse({ ...valid, code: 500 }, 9, payload), "rejected");
  assert.equal(classifyResponse({ ...valid, requestId: 10 }, 9, payload), "corrupt");
  assert.equal(classifyResponse({ ...valid, terminalNode: "wrong" }, 9, payload), "misrouted");
  assert.equal(
    classifyResponse({ ...valid, terminalNode: "worker-server-1" }, 9, payload, "worker-server-1"),
    "succeeded");
});

test("request uses the selected direct handler route", async () => {
  const client = new FakeClient();
  const pending = request(client, {}, 100, () => undefined, "connector.echoHandler.direct");
  assert.equal(client.route, "connector.echoHandler.direct");
  client.respond({ ok: true });
  await pending;
});

test("three-digit histogram buckets round upward deterministically", () => {
  assert.equal(quantizeUpperBound(999, 3), 999);
  assert.equal(quantizeUpperBound(1001, 3), 1010);
  assert.equal(quantizeUpperBound(21657, 3), 21700);
});

test("request classifies timeout and observes a late duplicate", async () => {
  const client = new FakeClient();
  let duplicates = 0;
  await assert.rejects(
    request(client, {}, 5, () => duplicates++),
    error => error.constructor.name === "TimeoutError");
  client.respond({ ok: true });
  assert.equal(duplicates, 1);
});

test("request rejects when the native client disconnects", async () => {
  const client = new FakeClient();
  const pending = request(client, {}, 100);
  client.emit("disconnect");
  await assert.rejects(pending, error => error.constructor.name === "DisconnectError");
});

class FakeClient extends EventEmitter {
  request(route, _message, callback) {
    this.route = route;
    this.callback = callback;
  }

  respond(value) {
    this.callback(value);
  }
}

const command = (requestTimeoutMilliseconds, drainTimeoutMilliseconds) => ({
  schemaVersion: "1", caseId: "test", framework: "pinus", workload: "frontdoor.echo",
  payloadSize: 32, concurrency: 2, connectionCount: 2, seed: 7,
  timing: { warmupMilliseconds: 0, measurementMilliseconds: 100, requestTimeoutMilliseconds, drainTimeoutMilliseconds },
  histogram: { unit: "microseconds", lowestDiscernibleValue: 1, highestTrackableValue: 60000000, significantDigits: 3 },
  endpoints: {}
});

for (const drain of [true, false]) {
  test(`outstanding calls use the earlier ${drain ? "drain" : "request"} deadline`, { timeout: 5000 }, async () => {
    const clients = [new FakeClient(), new FakeClient()];
    const result = await measureClients(clients, command(drain ? 10000 : 50, drain ? 50 : 10000));
    assert.equal(result.outcomes.started, 2);
    assert.equal(result.outcomes.canceledAtDrain, drain ? 2 : 0);
    assert.equal(result.outcomes.timedOut, drain ? 0 : 2);
    assert.equal(result.outcomes.disconnected, 0);
    assert.equal(result.outcomes.completed, 0);
    assert.equal(result.histogram.totalCount, 0);
    clients.forEach(client => assert.equal(client.listenerCount("disconnect"), 0));
  });
}

test("response during drain still completes normally", { timeout: 5000 }, async () => {
  const client = new FakeClient();
  client.request = (_route, message, callback) => setTimeout(() => callback({
    ...message, terminalNode: "connector-server-1"
  }), 250);
  const result = await measureClients([client], command(10000, 10000));
  assert.equal(result.outcomes.started, 1);
  assert.equal(result.outcomes.succeeded, 1);
  assert.equal(result.outcomes.canceledAtDrain, 0);
  assert.equal(result.histogram.totalCount, 1);
});

test("drain cancellation detaches listeners and ignores a late response", { timeout: 5000 }, async () => {
  const client = new FakeClient();
  let duplicates = 0;
  await assert.rejects(request(client, {}, 10000, () => duplicates++, undefined,
    process.hrtime.bigint() + 20_000_000n), error => error.constructor.name === "DrainCanceledError");
  assert.equal(client.listenerCount("disconnect"), 0);
  client.respond({ ok: true });
  assert.equal(duplicates, 0);
});
