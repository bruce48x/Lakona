"""Summarize only completed, validated runs; keep individual values and histograms."""
import html
import json
from pathlib import Path
import statistics
import sys

directory = Path(sys.argv[1]).resolve()
failures = [json.loads(p.read_text(encoding="utf-8-sig")) for p in sorted(directory.glob("*.failure.json"))]
results = []
for path in sorted(directory.glob("*.json")):
    value = json.loads(path.read_text(encoding="utf-8-sig"))
    if isinstance(value, dict) and value.get("phase") == "result":
        value["file"] = path.name
        results.append(value)
if not results:
    raise SystemExit("No completed measurements")

def median(items, key):
    return statistics.median(key(item) for item in items)

small = []
for transport in ("kcp", "tcp"):
    for size, concurrency in ((256, 1), (256, 32), (1024, 1), (1024, 32)):
        row = dict(transport=transport, size=size, concurrency=concurrency,
            failedRuns=sum(1 for f in failures if (f.get("mode"), f.get("transport"), f.get("size"), f.get("concurrency")) == ("small", transport, size, concurrency)))
        for version in ("baseline", "current"):
            runs = [r for r in results if (r["mode"], r["transport"], r["size"], r["concurrency"], r["version"]) ==
                    ("small", transport, size, concurrency, version) and r["repeat"] > 0]
            if not runs:
                continue
            row[version] = {"runs": len(runs), "count": sum(r["count"] for r in runs),
                "rps": median(runs, lambda r: r["rps"]),
                "rpsRange": [min(r["rps"] for r in runs), max(r["rps"] for r in runs)],
                **{p.replace("Us", "Ms"): median(runs, lambda r: r[p] / 1000) for p in ("p50Us", "p95Us", "p99Us", "maxUs")},
                "clientAllocatedPerOp": median(runs, lambda r: r["resources"]["allocatedBytes"] / r["count"]),
                "serverAllocatedPerOp": median(runs, lambda r: r["serverResources"]["allocatedBytes"] / r["count"]),
                "cpuUsPerOp": median(runs, lambda r: (r["resources"]["cpuSeconds"] + r["serverResources"]["cpuSeconds"]) * 1e6 / r["count"]),
            }
        if "current" in row and "baseline" in row:
            row["throughputChangePercent"] = 100 * (row["current"]["rps"] / row["baseline"]["rps"] - 1)
            small.append(row)

large = []
for transport in ("kcp", "tcp"):
    for size in (12 * 1024**2, 24 * 1024**2):
        for mode in ("upload", "download"):
            matching = [r for r in results if (r["transport"], r["size"], r["mode"]) == (transport, size, mode)]
            for count, concurrency in sorted({(r["count"], r["concurrency"]) for r in matching}):
                runs = [r for r in matching if (r["count"], r["concurrency"]) == (count, concurrency)]
                row = dict(transport=transport, sizeMiB=size / 1024**2, mode=mode,
                    count=count, concurrency=concurrency, totalMiB=size * count / 1024**2, runs=len(runs),
                    seconds=median(runs, lambda r: r["elapsed"]),
                    secondsRange=[min(r["elapsed"] for r in runs), max(r["elapsed"] for r in runs)],
                    mibPerSecond=median(runs, lambda r: r["size"] * r["count"] / 1024**2 / r["elapsed"]))
                for side, key in (("client", "resources"), ("server", "serverResources")):
                    row[side] = {
                        "peakMiB": [max(r[key]["peak"][i] for r in runs) / 1024**2 for i in range(3)],
                        "peakIncreaseMiB": [max(r[key]["peak"][i] - r[key]["start"][i] for r in runs) / 1024**2 for i in range(3)],
                        "allocatedMiB": median(runs, lambda r: r[key]["allocatedBytes"] / 1024**2),
                        "cpuSeconds": median(runs, lambda r: r[key]["cpuSeconds"]),
                        "gen2": [r[key]["collections"][2] for r in runs],
                    }
                large.append(row)

followups = {}
for name in ("tcp-recheck", "exception-diagnostic", "exception-diagnostic-stacks"):
    folder = directory / name
    if not folder.exists():
        continue
    records = []
    for path in folder.glob("*.json"):
        value = json.loads(path.read_text(encoding="utf-8-sig"))
        if isinstance(value, dict) and (value.get("phase") == "result" or path.name.endswith(".failure.json")):
            records.append(value)
    followups[name] = records
analysis_path = directory / "analysis.json"
analysis = json.loads(analysis_path.read_text(encoding="utf-8")) if analysis_path.exists() else []
summary = {"completedRuns": len(results), "failedRuns": len(failures), "failures": failures, "errorsInCompletedRuns": sum(r["errors"] for r in results),
           "analysis": analysis, "followups": followups,
           "environment": results[0]["environment"], "small": small, "large": large}
(directory / "summary.json").write_text(json.dumps(summary, indent=2), encoding="utf-8")

def table(headers, rows):
    return '<table><thead><tr>' + ''.join(f'<th>{html.escape(str(h))}</th>' for h in headers) + '</tr></thead><tbody>' + ''.join(
        '<tr>' + ''.join(f'<td>{html.escape(str(c))}</td>' for c in row) + '</tr>' for row in rows) + '</tbody></table>'

def num(value):
    return f"{value:,.3f}"

rows = []
for r in small:
    b, c = r["baseline"], r["current"]
    rows.append([r["transport"].upper(), r["size"], r["concurrency"], f'{b["runs"]}/{c["runs"]}',
        f'{b["rps"]:,.1f} → {c["rps"]:,.1f}', '不可判定（存在失败）' if r['failedRuns'] else f'{r["throughputChangePercent"]:+.1f}%',
        f'{num(b["p50Ms"])} → {num(c["p50Ms"])}', f'{num(b["p95Ms"])} → {num(c["p95Ms"])}',
        f'{num(b["p99Ms"])} → {num(c["p99Ms"])}',
        f'{b["rpsRange"][0]:,.1f}–{b["rpsRange"][1]:,.1f} / {c["rpsRange"][0]:,.1f}–{c["rpsRange"][1]:,.1f}'])
small_table = table(["传输", "字节", "并发", "重复 前/后", "吞吐 RPC/s 前 → 后", "变化", "p50 ms", "p95 ms", "p99 ms", "吞吐范围 前 / 后"], rows)
large_table = table(["传输", "单次 MiB", "方向", "并发", "每轮传输次数", "每轮总量 MiB", "独立运行次数", "整批耗时 s", "整批耗时范围 s", "有效 MiB/s"],
    [[r["transport"].upper(), r["sizeMiB"], r["mode"], r["concurrency"], r["count"], num(r["totalMiB"]), r["runs"], num(r["seconds"]),
      f'{num(r["secondsRange"][0])}–{num(r["secondsRange"][1])}', num(r["mibPerSecond"])] for r in large])
memory_rows = []
for r in large:
    for side in ("client", "server"):
        m = r[side]
        memory_rows.append([r["transport"], r["sizeMiB"], r["mode"], r["concurrency"], r["count"], r["runs"], side,
            *[f'{num(m["peakMiB"][i])} (+{num(m["peakIncreaseMiB"][i])})' for i in range(3)],
            num(m["allocatedMiB"]), num(m["cpuSeconds"]), str(m["gen2"])])
memory_table = table(["传输", "单次 MiB", "方向", "并发", "每轮传输次数", "独立运行次数", "进程", "托管占用峰值 (+增量) MiB", "Private 峰值 (+增量) MiB", "Working set 峰值 (+增量) MiB", "累计分配 MiB", "CPU s", "Gen2 次数"], memory_rows)
alloc_table = table(["传输", "字节", "并发", "客户端分配 B/op 前 → 后", "服务端分配 B/op 前 → 后", "两端 CPU μs/op 前 → 后"],
    [[r["transport"], r["size"], r["concurrency"],
      *[f'{num(r["baseline"][k])} → {num(r["current"][k])}' for k in ("clientAllocatedPerOp", "serverAllocatedPerOp", "cpuUsPerOp")]] for r in small])
raw_table = table(["文件", "版本", "传输", "字节", "并发", "模式", "次数", "秒", "RPC/s", "p99 ms"],
    [[r["file"], r["version"], r["transport"], r["size"], r["concurrency"], r["mode"], r["count"], num(r["elapsed"]), num(r["rps"]), num(r["p99Us"]/1000)] for r in results])
manifest = json.loads((directory / "manifest.json").read_text())
policy = manifest.get("measurement", {"seconds": 15, "warmupSeconds": 3, "repeats": 3})
failure_html = '<h2>失败样本：不得计为通过</h2>' + ''.join('<pre>' + html.escape(json.dumps(f, ensure_ascii=False, indent=2)) + '</pre>' for f in failures) if failures else ''
analysis_html = '<h2>结论与待处理问题</h2><ul>' + ''.join('<li>' + html.escape(item) + '</li>' for item in analysis) + '</ul>' if analysis else ''
followup_html = ''
if followups:
    followup_html = '<h2>独立复查与诊断（不混入主基准统计）</h2>'
    for name, records in followups.items():
        followup_html += '<h3>' + name + '</h3>'
        for record in records:
            compact = {k: record[k] for k in ("version", "repeat", "transport", "mode", "size", "concurrency", "count", "elapsed", "rps", "error") if k in record}
            if name.startswith("exception-diagnostic") and record.get("phase") == "result":
                compact["clientExceptionsIncludingWarmup"] = record["resources"].get("diagnosticExceptions")
                compact["serverExceptionsIncludingWarmup"] = record["serverResources"].get("diagnosticExceptions")
                if "diagnosticExceptionStacks" in record["resources"]:
                    compact["clientStacks"] = record["resources"]["diagnosticExceptionStacks"]
                    compact["serverStacks"] = record["serverResources"]["diagnosticExceptionStacks"]
            followup_html += '<pre>' + html.escape(json.dumps(compact, ensure_ascii=False, indent=2)) + '</pre>'
extra = directory / "environment-extra.json"
extra_text = extra.read_text() if extra.exists() else "See manifest and environment."
page = f'''<!doctype html><html lang="zh-CN"><meta charset="utf-8"><title>Lakona RELIABILITY-022 性能验证</title>
<style>body{{font:15px/1.65 system-ui,sans-serif;max-width:1500px;margin:32px auto;padding:0 24px;color:#172433;background:#f6f8fb}}h1,h2{{color:#123a58}}table{{border-collapse:collapse;background:white;width:100%;font-size:13px;margin:18px 0}}td,th{{border:1px solid #dbe2e8;padding:9px;text-align:left}}th{{background:#e9f0f6}}pre{{white-space:pre-wrap;overflow-wrap:anywhere;background:#e9f0f6;padding:14px}}.note{{padding:16px;background:#fff4d9;border-left:4px solid #d0a034}}details{{margin:20px 0}}</style>
<h1>Lakona RELIABILITY-022 性能验证</h1><p>本机开发验证 · {len(results)} 个完整测量进程对 · 失败工况 {len(failures)} · 成功样本内错误 {summary['errorsInCompletedRuns']} · Release / .NET 10 / MemoryPack</p>{failure_html}
<div class="note">结果仅代表本机回环、单连接和默认参数。后台仍有 IDE、虚拟机及桌面应用；没有隔离机器或绑定 CPU。小消息采用闭环固定并发，不能解释成固定到达率下的线上尾延迟。</div>{analysis_html}
<h2>测量口径</h2><ul><li>前：{manifest['baseline']}；后：本次修复的源码快照。current-sha256.json、changes.patch、两份源码与构建日志均保留在同目录。</li>
<li>客户端和服务端是独立进程，经过真实 RpcClientRuntime、RpcSession、MemoryPack、共享发送队列及 TCP/KCP。没有绕过 RPC 分发；不包含 Game 路由、跨节点批处理或可靠 Outbox。</li>
<li>每个小消息工况独立启动；预热 {policy['warmupSeconds']} 秒，测量 {policy['seconds']} 秒并等待已发请求完成；计划 {policy['repeats']} 次重复交替 A/B 顺序。表中是成功测量各次吞吐和分位数的中位数，失败不会填入吞吐平均数，也不能忽略为成功；完整分布保留在每个 JSON 的 1% 精度直方图中。</li>
<li>单并发 KCP 每次约 150 个请求，p99 的样本很少；请结合每次范围和原始结果，避免把一次尾部抖动解释为稳定回归。</li>
<li>大消息每个新进程对先做小消息预热，再执行一轮传输；每轮传输次数采用原始记录的实际完成总次数 count，包含所有并发请求。每轮总量为单次大小 × count，有效吞吐为每轮总量 ÷ 整批耗时，再对独立运行取中位数。不同传输次数或并发数分别汇总，独立运行次数不是每轮传输次数。</li>
<li>上传返回校验成功后的长度；下载等待完整 payload 校验完成。整批耗时包含序列化、调度和校验，不是单次延迟或单纯网络计时。单次延迟分位数仍来自原始直方图；不能用整批耗时除以次数代替 p99。</li>
<li>默认 KCP 消息模式、窗口、拥塞控制和调度参数不变；无压缩、无加密；keepalive 关闭。实际独立运行次数以表格为准；少量运行用于观察尺寸增长，不据此计算稳定尾延迟。</li></ul>
<h2>普通小消息：修改前后</h2>{small_table}<h2>分配与 CPU</h2>{alloc_table}
<h2>大消息整批完成时间与吞吐</h2>{large_table}<h2>大消息内存</h2>
<p>每 20 ms 采样，不强制 GC；报告各次最高峰值与相对测量起点的最高增量。两端的基准 payload 均在测量前分配，绝对值包含它，增量不包含它。托管占用含尚未回收的垃圾，不能等同存活对象或泄漏。Private 是进程私有提交量，Working set 是驻留量；短于采样周期的峰值可能漏采，尤其是 TCP。采样使用专用线程和 Windows 原生内存计数器，避免每次采样产生托管数组。</p>{memory_table}
{followup_html}<h2>环境与复现</h2><pre>{html.escape(json.dumps(summary['environment'], indent=2))}\n{html.escape(extra_text)}</pre><pre>{html.escape(json.dumps(manifest, indent=2))}</pre>
<details><summary>全部原始测量索引</summary>{raw_table}</details></html>'''
(directory / "report.html").write_text(page, encoding="utf-8")
print(f"Report: {directory / 'report.html'}")
print(f"Summary: {directory / 'summary.json'}")
print(f"Completed: {len(results)} | Failed: {len(failures)}")
