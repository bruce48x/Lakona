export const targetCount = 256;

export function targetKey(requestId: number): string {
  return `entity/${requestId % targetCount}`;
}

export function ownerNumber(key: string): 1 | 2 {
  let hash = 0x811c9dc5;
  for (const value of Buffer.from(key, "utf8")) {
    hash ^= value;
    hash = Math.imul(hash, 0x01000193) >>> 0;
  }

  return (hash & 1) === 0 ? 1 : 2;
}

export function workerNode(key: string): string {
  return `worker-server-${ownerNumber(key)}`;
}
