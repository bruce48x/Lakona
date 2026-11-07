"""Deterministic local UDP impairment relay for KCP benchmark diagnostics."""
import heapq
import random
import select
import socket
import struct
import threading
import time


class Relay:
    def __init__(self, server_port, delay=.02, mbps=10, loss=.001):
        self.server = ("127.0.0.1", server_port)
        self.client = None
        self.delay = delay
        self.rate = mbps * 1_000_000 / 8
        self.loss = loss
        self.socket = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self.socket.setsockopt(socket.SOL_SOCKET, socket.SO_RCVBUF, 1024 * 1024)
        self.socket.bind(("127.0.0.1", 0))
        self.port = self.socket.getsockname()[1]
        self.stop = threading.Event()
        self.stats = {"received": [0, 0], "sent": [0, 0], "dropped": [0, 0], "maxQueued": 0}
        self.error = None
        self.thread = threading.Thread(target=self.run)
        self.thread.start()

    def run(self):
        pending = []
        sequence = 0
        available = [0., 0.]
        randomizer = random.Random(42)
        try:
            while not self.stop.is_set():
                now = time.monotonic()
                while pending and pending[0][0] <= now:
                    _, _, data, target, direction = heapq.heappop(pending)
                    self.socket.sendto(data, target)
                    self.stats["sent"][direction] += 1
                timeout = min(.01, max(0, pending[0][0] - now)) if pending else .01
                if not select.select([self.socket], [], [], timeout)[0]:
                    continue
                data, source = self.socket.recvfrom(65536)
                direction = int(source == self.server)
                if direction:
                    if self.client is None:
                        continue
                    target = self.client
                    # Keep the client on this relay after the bootstrap ACK.
                    if len(data) == 12 and data[:4] == b"UACK":
                        data = data[:8] + struct.pack("<i", self.port)
                else:
                    self.client = source
                    target = self.server
                self.stats["received"][direction] += 1
                handshake = len(data) in (8, 12) and data[:4] in (b"UKCP", b"UACK", b"UNAK")
                if not handshake and randomizer.random() < self.loss:
                    self.stats["dropped"][direction] += 1
                    continue
                now = time.monotonic()
                available[direction] = max(available[direction], now) + len(data) / self.rate
                due = available[direction] + self.delay + randomizer.uniform(0, .002)
                sequence += 1
                heapq.heappush(pending, (due, sequence, data, target, direction))
                self.stats["maxQueued"] = max(self.stats["maxQueued"], len(pending))
                if len(pending) > 4096:
                    raise RuntimeError("Relay queue exceeded bound")
        except Exception as error:
            self.error = repr(error)

    def close(self):
        self.stop.set()
        self.thread.join(5)
        self.socket.close()
