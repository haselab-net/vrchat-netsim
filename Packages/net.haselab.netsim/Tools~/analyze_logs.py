"""Analyzes the VRChat client logs of a multi-client test with a NetSim bot (real VRChat client).

  python analyze_logs.py --since "2026-10-06 10:28" [--must-match good,goals] [--out report.md]
  python analyze_logs.py --since "2026-10-06 10:28" --watch      # wait until every bot is done, then report

Bot log lines (see Documentation~/README.md, "Testing in the real VRChat client"):
  [NSBOT] t=<seconds> p=<player id> ready ...
  [NSBOT] t=... p=... act <what>
  [NSBOT] t=... p=... carry start <what> | carry end (<reason>)
  [NSBOT] t=... p=... state key=value key=value ...      (synced world state as this client sees it)
  [NSBOT] t=... p=... done                               (the bot stopped acting)

Clients started in the same second share one log file, so lines are grouped by the player id the bot writes, not by
file. A client whose log stopped long before the others' is treated as having left (e.g. killed to test master leave)
and is not compared. The report lists each client's last state and every key whose value differs between clients.
"""
import argparse
import datetime
import glob
import os
import re
import sys
import time

ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
ap.add_argument("--dir", default=os.path.expandvars(r"%USERPROFILE%\AppData\LocalLow\VRChat\VRChat"))
ap.add_argument("--since", default=None, help="only log files created at/after this time (YYYY-MM-DD HH:MM)")
ap.add_argument("--prefix", default="NSBOT")
ap.add_argument("--must-match", default="", help="comma-separated state keys that must be equal on every client (verdict)")
ap.add_argument("--left-seconds", type=float, default=60, help="a client silent this long before the newest line has left")
ap.add_argument("--out", default=None)
ap.add_argument("--watch", action="store_true", help="poll until every present client is done and states are stable")
ap.add_argument("--interval", type=float, default=30)
ap.add_argument("--settle", type=float, default=30, help="--watch: seconds without state changes after all are done")
ap.add_argument("--timeout", type=float, default=60, help="--watch: give up after this many minutes")
opt = ap.parse_args()

line_re = re.compile(r"^(\d{4}\.\d{2}\.\d{2} \d{2}:\d{2}:\d{2}) .*\[" + re.escape(opt.prefix) + r"\] t=([\d.]+) p=(-?\d+) (.*)$")
stamp_re = re.compile(r"^(\d{4}\.\d{2}\.\d{2} \d{2}:\d{2}:\d{2}) ")
error_marks = ("UdonVMException", "will be halted", "Udon runtime exception")


def ts(s):
    return datetime.datetime.strptime(s, "%Y.%m.%d %H:%M:%S")


def log_files():
    since = datetime.datetime.strptime(opt.since, "%Y-%m-%d %H:%M") if opt.since else None
    res = []
    for f in sorted(glob.glob(os.path.join(opt.dir, "output_log_*.txt"))):
        m = re.search(r"output_log_(\d{4}-\d{2}-\d{2})_(\d{2})-(\d{2})-(\d{2})", f)
        created = datetime.datetime.strptime(m.group(1) + " " + ":".join(m.group(2, 3, 4)), "%Y-%m-%d %H:%M:%S") if m else None
        if since and created and created < since:
            continue
        res.append(f)
    return res


def parse_state(s):
    return dict(kv.split("=", 1) for kv in s.split() if "=" in kv)


def collect():
    files = log_files()
    clients, errors, newest = {}, [], None
    for f in files:
        for line in open(f, encoding="utf-8", errors="replace"):
            line = line.rstrip("\n")
            m = stamp_re.match(line)
            if m:
                t = ts(m.group(1))
                newest = t if newest is None or t > newest else newest
            if any(e in line for e in error_marks) and len(errors) < 50:
                errors.append(os.path.basename(f) + ": " + line[:220])
            m = line_re.match(line)
            if not m:
                continue
            stamp, _, p, msg = m.groups()
            c = clients.setdefault(int(p), {"files": set(), "ready": None, "done": None, "state": None, "state_at": None,
                                           "last": None, "acts": 0, "carries": 0, "carry_end": {}})
            c["files"].add(os.path.basename(f))
            c["last"] = ts(stamp)
            if msg.startswith("ready"):
                c["ready"] = stamp
            elif msg.startswith("done"):
                c["done"] = stamp
            elif msg.startswith("state "):
                st = parse_state(msg[6:])
                if st != c["state"]:
                    c["state_at"] = ts(stamp)
                c["state"] = st
            elif msg.startswith("act "):
                c["acts"] += 1
            elif msg.startswith("carry start"):
                c["carries"] += 1
            elif msg.startswith("carry end"):
                why = re.search(r"carry end \(([^)]*)\)", msg)
                why = why.group(1) if why else "?"
                c["carry_end"][why] = c["carry_end"].get(why, 0) + 1
    for c in clients.values():
        c["left"] = newest is not None and c["last"] is not None and (newest - c["last"]).total_seconds() > opt.left_seconds
    return files, clients, errors


def report(files, clients, errors):
    present = {p: c for p, c in clients.items() if not c["left"] and c["state"] is not None}
    keys = sorted({k for c in present.values() for k in c["state"]})
    differing = [k for k in keys if len({c["state"].get(k) for c in present.values()}) > 1]
    out = ["# NetSim bot run (real VRChat client)",
           f"- log files: {', '.join(os.path.basename(f) for f in files)}",
           f"- clients (bots): {len(clients)}, compared: {len(present)}", "",
           "| player | ready | done | left | actions | carries (end reasons) | last state |",
           "|---|---|---|---|---|---|---|"]
    for p in sorted(clients):
        c = clients[p]
        ends = ", ".join(f"{k}={v}" for k, v in sorted(c["carry_end"].items()))
        st = " ".join(f"{k}={v}" for k, v in (c["state"] or {}).items())
        out.append(f"| p{p} | {c['ready'] or '-'} | {c['done'] or '-'} | {'yes' if c['left'] else ''} | {c['acts']} | {c['carries']} ({ends}) | `{st}` |")
    out += ["", f"## State differences between clients ({len(differing)} keys)"]
    if differing:
        out.append("| key | " + " | ".join(f"p{p}" for p in sorted(present)) + " |")
        out.append("|---|" + "---|" * len(present))
        for k in differing:
            out.append(f"| {k} | " + " | ".join(str(present[p]["state"].get(k, "-")) for p in sorted(present)) + " |")
    else:
        out.append("- none: every compared client ends with the same state")
    must = [k for k in opt.must_match.split(",") if k]
    if must:
        out += ["", "## Verdict"]
        ok = len(present) > 1
        for k in must:
            same = k in keys and k not in differing
            ok &= same
            out.append(f"- [{'x' if same else ' '}] `{k}` is equal on every client")
        out.append(f"- [{'x' if not errors else ' '}] no Udon errors")
        ok &= not errors
        out.append(f"- **{'PASS' if ok else 'FAIL'}**")
    out += ["", f"## Udon errors ({len(errors)})"] + [f"- {e}" for e in errors[:20]]
    return "\n".join(out)


def main():
    if opt.watch:
        start = time.time()
        while True:
            files, clients, errors = collect()
            present = [c for c in clients.values() if not c["left"]]
            all_done = bool(present) and all(c["done"] for c in present)
            last_change = max((c["state_at"] for c in present if c["state_at"]), default=None)
            now = max((c["last"] for c in present if c["last"]), default=None)
            stable = last_change is not None and now is not None and (now - last_change).total_seconds() >= opt.settle
            print(time.strftime("%H:%M:%S"), f"clients={len(clients)} present={len(present)} done={sum(1 for c in present if c['done'])} "
                  f"errors={len(errors)} stable={stable}", flush=True)
            if all_done and stable:
                break
            if time.time() - start > opt.timeout * 60:
                print("TIMEOUT")
                break
            time.sleep(opt.interval)
    files, clients, errors = collect()
    text = report(files, clients, errors)
    print(text)
    if opt.out:
        with open(opt.out, "w", encoding="utf-8") as f:
            f.write(text + "\n")
    if "**FAIL**" in text:
        sys.exit(1)


if __name__ == "__main__":
    main()
