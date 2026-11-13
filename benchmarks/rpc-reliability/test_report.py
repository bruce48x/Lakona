"""Exercise the report CLI with synthetic records, without running a benchmark."""
from html.parser import HTMLParser
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[2]


class Tables(HTMLParser):
    def __init__(self, page):
        super().__init__()
        self.tables = []
        self.cell = None
        self.feed(page)

    def handle_starttag(self, tag, attrs):
        if tag == "table":
            self.tables.append([])
        elif tag == "tr":
            self.tables[-1].append([])
        elif tag in ("td", "th"):
            self.cell = ""

    def handle_data(self, data):
        if self.cell is not None:
            self.cell += data

    def handle_endtag(self, tag):
        if tag in ("td", "th"):
            self.tables[-1][-1].append(self.cell)
            self.cell = None

    def rows(self, column):
        table = next(table for table in self.tables if column in table[0])
        return [dict(zip(table[0], row)) for row in table[1:]]


def record(count=1, elapsed=3, transport="tcp", mode="upload", concurrency=1):
    resources = dict(allocatedBytes=count * 1024**2, cpuSeconds=count,
                     start=[0, 0, 0], peak=[count * 1024**2] * 3, collections=[0, 0, count])
    return dict(phase="result", transport=transport, mode=mode, size=12 * 1024**2,
                concurrency=concurrency, elapsed=elapsed, count=count, rps=count / elapsed,
                p50Us=1234, p95Us=2345, p99Us=3456, maxUs=4567,
                resources=resources, serverResources=resources, version="current",
                repeat=1, environment={"synthetic": True}, errors=0)


class ReportTests(unittest.TestCase):
    def generate(self, records, failures=()):
        scratch = ROOT / "artifacts" / "rpc-report-tests"
        scratch.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(dir=scratch) as temporary:
            directory = Path(temporary)
            (directory / "manifest.json").write_text(json.dumps({"baseline": "synthetic"}), encoding="utf-8")
            for index, value in enumerate(records):
                (directory / f"run-{index}.json").write_text(json.dumps(value), encoding="utf-8")
            for index, value in enumerate(failures):
                (directory / f"run-{index}.failure.json").write_text(json.dumps(value), encoding="utf-8")
            subprocess.run([sys.executable, "-X", "utf8", str(Path(__file__).with_name("report.py")), str(directory)],
                           check=True, capture_output=True, text=True, encoding="utf-8", timeout=20)
            return (json.loads((directory / "summary.json").read_text(encoding="utf-8")),
                    (directory / "report.html").read_text(encoding="utf-8"))

    def test_throughput_uses_completed_count_for_upload_and_download(self):
        for transport in ("tcp", "kcp"):
            for mode in ("upload", "download"):
                for count, expected in ((1, 4), (3, 12)):
                    with self.subTest(transport=transport, mode=mode, count=count):
                        summary, page = self.generate([record(count, transport=transport, mode=mode)])
                        self.assertEqual(expected, summary["large"][0]["mibPerSecond"])
                        self.assertEqual(f"{expected:.3f}", Tables(page).rows("有效 MiB/s")[0]["有效 MiB/s"])

    def test_different_batch_counts_and_concurrency_do_not_mix(self):
        summary, page = self.generate([record(), record(3, 9), record(3, 3, concurrency=3)])
        self.assertEqual(3, len(summary["large"]))
        rows = {(r["count"], r["concurrency"]): r for r in summary["large"]}
        self.assertEqual(3, rows[1, 1]["seconds"])
        self.assertEqual(9, rows[3, 1]["seconds"])
        self.assertEqual(12, rows[3, 3]["mibPerSecond"])
        self.assertEqual(1, rows[1, 1]["client"]["allocatedMiB"])
        self.assertEqual(3, rows[3, 1]["client"]["allocatedMiB"])
        tables = Tables(page)
        self.assertEqual(3, len(tables.rows("有效 MiB/s")))
        self.assertEqual(6, len(tables.rows("累计分配 MiB")))

    def test_independent_runs_keep_per_run_rates_and_batch_totals(self):
        summary, page = self.generate([record(3, 1), record(3, 3)])
        row = summary["large"][0]
        self.assertEqual(24, row["mibPerSecond"])
        self.assertEqual(2, row["seconds"])
        self.assertEqual([1, 3], row["secondsRange"])
        self.assertEqual(2, row["runs"])
        self.assertEqual(3, row["count"])
        self.assertEqual(36, row["totalMiB"])
        visible = Tables(page).rows("有效 MiB/s")[0]
        self.assertEqual("3", visible["每轮传输次数"])
        self.assertEqual("36.000", visible["每轮总量 MiB"])
        self.assertEqual("2", visible["独立运行次数"])
        self.assertEqual("2.000", visible["整批耗时 s"])
        raw_rows = Tables(page).rows("文件")
        self.assertEqual(2, len(raw_rows))
        self.assertEqual(["3.456", "3.456"], [r["p99 ms"] for r in raw_rows])
        self.assertNotIn("随后只传一个大消息", page)

    def test_small_results_and_failure_accounting_stay_intact(self):
        current = record(30, mode="small") | {"size": 256}
        baseline = current | {"version": "baseline", "rps": 5}
        failure = dict(mode="upload", transport="tcp", size=12 * 1024**2, error="synthetic failure")
        summary, page = self.generate([baseline, current, record(3)], [failure])
        self.assertEqual(3, summary["completedRuns"])
        self.assertEqual(1, summary["failedRuns"])
        self.assertEqual(100, summary["small"][0]["throughputChangePercent"])
        self.assertEqual(1, summary["large"][0]["runs"])
        self.assertIn("synthetic failure", page)


if __name__ == "__main__":
    unittest.main()
