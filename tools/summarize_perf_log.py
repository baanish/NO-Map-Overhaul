"""Summarizes NO Map Overhaul's Log performance lines from a Player.log or LogOutput.log.

Prints one row per bucket of 5 s windows (default 60, about five minutes): frame rate, the mod's cost,
Unity's canvas and render load, the team, drawings and sharing traffic, and each teammate's Steam link.
It also counts slow Steam calls and frame gaps. Usage:

    python tools/summarize_perf_log.py <log> [--bucket N]
"""
import argparse
import re
import statistics

PERF = re.compile(
    r"Perf [\d.]+ s, mod on: (?P<frames>\d+) frames, (?P<fps>[\d.]+) fps avg .*?1% low (?P<low>[\d.]+) fps, "
    r"worst frame (?P<worst>[\d.]+) ms\. Mod per frame (?P<mod>[\d.]+) ms avg, (?P<modworst>[\d.]+) ms worst\..*?"
    r"Mod allocated (?P<alloc>[\d.]+) KB per frame\. GC (?P<gc>\d+)x, heap (?P<heap>\d+) of \d+ MB\. "
    r"Drawings (?P<own>\d+) own, (?P<recv>\d+) received, (?P<shown>\d+) shown\. "
    r"Mod graphics (?P<drawn>\d+) drawn, (?P<tmp>\d+) TMP, (?P<held>\d+) held\. "
    r"Sharing sent (?P<sent>\d+) \((?P<sentkb>[\d.]+) KB\), received (?P<rx>\d+) \((?P<rxkb>[\d.]+) KB\), "
    r"(?P<applied>\d+) snapshots applied, Steam calls (?P<steam>[\d.]+) ms, worst (?P<steamworst>[\d.]+) ms\."
    r"(?: Mod ms per frame by part, average/worst frame: (?P<parts>.*)\.)?")
DETAIL = re.compile(
    r"Perf detail: Unity canvas update (?P<canvas>[\d.]+) ms avg.*?per frame (?P<batches>\d+) batches.*?"
    r"(?P<verts>\d+) vertices\. Rebuilt label layouts (?P<labels>\d+)x, meshes (?P<meshes>\d+)x, "
    r"received drawings (?P<rebuilt>\d+)x\.")
SESSION = re.compile(r"(?P<team>\d+) on the team, (?P<icons>\d+) map icons, (?P<view>[a-z ]+?)(?:, mission time (?P<time>[\d:]+))?\.")
PEER = re.compile(
    r"peer (?P<id>\d+) (?P<ready>ready|not ready): drawings (?P<held>\d+) held, (?P<shown>\d+) shown; "
    r"(?P<applied>\d+) snapshots applied; in (?P<inn>\d+) \([\d.]+ KB\), out (?P<out>\d+) \([\d.]+ KB\)"
    r"(?:; Steam (?P<state>\w+), ping (?P<ping>-?\d+) ms, quality (?P<quality>[^,]+), "
    r"reliable (?P<pending>\d+) B pending, (?P<unacked>\d+) B unacked, queue (?P<queue>[\d.]+) ms)?")
PART = re.compile(r"([\w ]+?) ([\d.]+)/([\d.]+)")


def mean(values):
    return statistics.fmean(values) if values else 0.0


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("log")
    parser.add_argument("--bucket", type=int, default=60, help="5 s windows per row")
    args = parser.parse_args()

    rows, details, peers, warnings = [], [], [], {"blocked": [], "gap": []}
    with open(args.log, encoding="utf-8", errors="replace") as log:
        for line in log:
            if "NO Map Overhaul]" not in line:
                continue
            if (m := PERF.search(line)):
                rows.append(m.groupdict())
                details.append(None)
                peers.append([])
            elif (m := DETAIL.search(line)) and rows:
                details[-1] = m.groupdict()
                if (s := SESSION.search(line)):
                    details[-1].update(s.groupdict())
            elif "Perf teammates:" in line and rows:
                peers[-1] = [p.groupdict() for p in PEER.finditer(line)]
            elif "blocked the game for" in line:
                warnings["blocked"].append(line.strip().split("] ", 1)[-1])
            elif "No frame for" in line:
                warnings["gap"].append(line.strip().split("] ", 1)[-1])

    if not rows:
        print("No Log performance lines found.")
        return

    print("rows: mission time | fps avg / 1% low | mod ms avg / worst | top part | canvas ms | batches | "
          "team / icons | drawings own/recv/shown | graphics drawn/held | sharing in/out per 5 s | teammates ready, "
          "worst ping, worst queue")
    for start in range(0, len(rows), args.bucket):
        chunk = rows[start:start + args.bucket]
        chunk_details = [d for d in details[start:start + args.bucket] if d]
        chunk_peers = [p for ps in peers[start:start + args.bucket] for p in ps]
        f = lambda k: [float(r[k]) for r in chunk]
        parts = {}
        for r in chunk:
            for name, avg, _ in PART.findall(r.get("parts") or ""):
                parts.setdefault(name.strip(" ,"), []).append(float(avg))
        top = max(parts.items(), key=lambda kv: mean(kv[1]), default=("-", [0]))
        times = [d["time"] for d in chunk_details if d.get("time")]
        team = [int(d["team"]) for d in chunk_details if d.get("team")]
        icons = [int(d["icons"]) for d in chunk_details if d.get("icons")]
        last = chunk[-1]
        ready = {p["id"] for p in chunk_peers if p["ready"] == "ready"}
        pings = [int(p["ping"]) for p in chunk_peers if p.get("ping") and int(p["ping"]) >= 0]
        queues = [float(p["queue"]) for p in chunk_peers if p.get("queue") and float(p["queue"]) < 1e9]
        print(f"{times[-1] if times else '-':>8} | {mean(f('fps')):5.1f} / {mean(f('low')):5.1f} | "
              f"{mean(f('mod')):.3f} / {max(f('modworst')):6.2f} | {top[0]} {mean(top[1]):.3f} | "
              f"{mean([float(d['canvas']) for d in chunk_details]):4.2f} | "
              f"{int(statistics.median([int(d['batches']) for d in chunk_details])) if chunk_details else 0:6d} | "
              f"{max(team, default=0):2d} / {max(icons, default=0):4d} | "
              f"{last['own']}/{last['recv']}/{last['shown']} | {last['drawn']}/{last['held']} | "
              f"{mean(f('rx')):.0f}/{mean(f('sent')):.0f} | {len(ready)}, {max(pings, default=-1)} ms, "
              f"{max(queues, default=0):.0f} ms")

    fps = [float(r["fps"]) for r in rows]
    mod = [float(r["mod"]) for r in rows]
    print(f"\n{len(rows)} windows: fps {mean(fps):.1f} avg, mod {mean(mod):.3f} ms avg, "
          f"{max(float(r['modworst']) for r in rows):.2f} ms worst frame, "
          f"{sum(int(r['applied']) for r in rows)} snapshots applied, "
          f"{max(float(r['steamworst']) for r in rows):.2f} ms worst Steam call.")
    for kind, lines in warnings.items():
        if lines:
            print(f"{len(lines)} {kind} warnings, e.g. {lines[0]}")


if __name__ == "__main__":
    main()
