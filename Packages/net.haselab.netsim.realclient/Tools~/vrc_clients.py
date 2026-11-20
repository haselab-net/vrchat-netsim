"""Controls the VRChat clients of a local multi-client test started by the SDK "Build & Test" (Windows).

  python vrc_clients.py list                 # running VRChat clients, their log file and the bot's player id
  python vrc_clients.py launch [n] [gap]     # start n more clients into the same local test instance (late join)
  python vrc_clients.py kill <player>        # terminate the client whose bot is player <player> (e.g. the master)
  python vrc_clients.py killall              # terminate every VRChat client

Options: --prefix NSBOT (log prefix the bot writes), --dir <VRChat log directory>.

The launch arguments (world file, room id, flags) are taken from the newest client log ("Launching with args").
A client is matched to its log by start time. Clients started in the same second share one log file, so start them
with a gap (default 6 s) when they must be told apart.
"""
import argparse
import datetime
import glob
import os
import re
import subprocess
import time

ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
ap.add_argument("command", nargs="?", default="list", choices=["list", "launch", "kill", "killall"])
ap.add_argument("args", nargs="*")
ap.add_argument("--prefix", default="NSBOT")
ap.add_argument("--dir", default=os.path.expandvars(r"%USERPROFILE%\AppData\LocalLow\VRChat\VRChat"))
opt = ap.parse_args()


def ps(cmd):
    return subprocess.run(["powershell", "-NoProfile", "-Command", cmd], capture_output=True, text=True).stdout


def processes():
    out = ps("Get-Process VRChat -ErrorAction SilentlyContinue | % { '{0} {1}' -f $_.Id, $_.StartTime.ToString('yyyy-MM-dd HH:mm:ss') }")
    res = []
    for line in out.splitlines():
        parts = line.strip().split(" ", 1)
        if len(parts) == 2:
            res.append((int(parts[0]), datetime.datetime.strptime(parts[1], "%Y-%m-%d %H:%M:%S")))
    return res


def logs():
    res = []
    for f in glob.glob(os.path.join(opt.dir, "output_log_*.txt")):
        m = re.search(r"output_log_(\d{4}-\d{2}-\d{2})_(\d{2})-(\d{2})-(\d{2})", f)
        if m:
            res.append((f, datetime.datetime.strptime(m.group(1) + " " + ":".join(m.group(2, 3, 4)), "%Y-%m-%d %H:%M:%S")))
    return sorted(res, key=lambda x: x[1])


def players_in(f):
    ready = re.compile(r"\[" + re.escape(opt.prefix) + r"\] t=[\d.]+ p=(\d+) ready")
    ids = set()
    for line in open(f, encoding="utf-8", errors="replace"):
        m = ready.search(line)
        if m:
            ids.add(int(m.group(1)))
    return sorted(ids)


def match():
    """[(pid, start time, log file, [player ids])]"""
    lg = logs()
    res = []
    for pid, st in processes():
        best = None
        for f, t in lg:
            d = (t - st).total_seconds()
            if -2 <= d <= 10 and (best is None or abs(d) < abs(best[1])):
                best = (f, d)
        res.append((pid, st, best[0] if best else None, players_in(best[0]) if best else []))
    return res


def launch_args():
    for f, _ in reversed(logs()):
        text = open(f, encoding="utf-8", errors="replace").read()
        m = re.search(r"Launching with args: \d+\n((?:.*Arg: .*\n)+)", text)
        if m:
            args = [re.sub(r"^.*Arg: ", "", line) for line in m.group(1).splitlines()]
            return [a for a in args if not a.startswith("--startup-begin-ts")]
    raise SystemExit("no launch arguments found in the client logs")


def main():
    if opt.command == "list":
        for pid, st, f, ids in match():
            print(pid, st, os.path.basename(f) if f else "-", "players", ids)
    elif opt.command == "launch":
        n = int(opt.args[0]) if opt.args else 1
        gap = float(opt.args[1]) if len(opt.args) > 1 else 6
        args = launch_args()
        for i in range(n):
            subprocess.Popen(args, cwd=os.path.dirname(args[0]))
            print("launched", " ".join(a[:60] for a in args[1:3]))
            if i < n - 1:
                time.sleep(gap)
    elif opt.command == "kill":
        p = int(opt.args[0])
        cands = [(pid, f, ids) for pid, st, f, ids in match() if ids == [p]]
        if not cands:
            shared = [(pid, ids) for pid, st, f, ids in match() if p in ids]
            raise SystemExit(f"player {p} not uniquely identified (shared log: {shared})")
        for pid, f, ids in cands:
            ps(f"Stop-Process -Id {pid} -Force")
            print("killed pid", pid, "player", p, os.path.basename(f))
    elif opt.command == "killall":
        ps("Get-Process VRChat -ErrorAction SilentlyContinue | Stop-Process -Force")
        print("killed all")


if __name__ == "__main__":
    main()
