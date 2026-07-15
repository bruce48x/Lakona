const assert = require("node:assert/strict");
const { EventEmitter } = require("node:events");
const test = require("node:test");
const {
  classifyResponse,
  createPayload,
  quantizeUpperBound,
  request
} = require("../dist/driver");

test("payload generation is deterministic at both version 1 sizes", () => {
  for (const size of [32, 256]) {
    const first = createPayload(20260715, 42, size);
    const second = createPayload(20260715, 42, size);
    assert.equal(first.length, size);
    assert.deepEqual(first, second);
  }
});

test("response classification distinguishes every completed outcome", () => {
  const payload = createPayload(7, 9, 32);
  const valid = { requestId: 9, payload, terminalNode: "connector-server-1" };
  assert.equal(classifyResponse(valid, 9, payload), "succeeded");
  assert.equal(classifyResponse({ ...valid, code: 500 }, 9, payload), "rejected");
  assert.equal(classifyResponse({ ...valid, requestId: 10 }, 9, payload), "corrupt");
  assert.equal(classifyResponse({ ...valid, terminalNode: "wrong" }, 9, payload), "misrouted");
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
  request(_route, _message, callback) {
    this.callback = callback;
  }

  respond(value) {
    this.callback(value);
  }
}
