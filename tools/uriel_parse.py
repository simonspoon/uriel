#!/usr/bin/env python3
"""Reassemble Uriel's chunked JSON-L stream from a captured console log.

Uriel prints one line per chunk:
    @@URIEL@@ {"p":"<proc nonce>","s":<seq>,"c":[<i>,<n>],"d":"<base64 fragment>"}

This stitches fragments by (proc, seq), base64-decodes, and emits one inner
JSON record per line — ready for jq, pandas, etc. Each record gets "seq" and,
when the envelope carries one, "proc": a per-process nonce, so one capture
spanning several launches splits cleanly by process (seq restarts each launch).

Usage:
    adb logcat | python3 uriel_parse.py          # live (Android)
    python3 uriel_parse.py < captured.log         # from a file
    python3 uriel_parse.py --sentinel '§URIEL§'   # custom marker
"""
import argparse
import base64
import json
import sys

def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--sentinel", default="@@URIEL@@")
    ap.add_argument("--raw", action="store_true",
                    help="emit reassembled inner JSON verbatim (no re-pretty)")
    args = ap.parse_args()

    pending: dict[tuple, dict] = {}   # (proc, seq) -> {n, parts: {i: frag}}

    for line in sys.stdin:
        idx = line.find(args.sentinel)
        if idx >= 0:
            payload = line[idx + len(args.sentinel):].strip()
        else:
            # Fallback: some sinks (iOS os_log) may mangle the sentinel prefix.
            # Recover by locating the chunk envelope's start directly
            # ({"p": since 0.2; {"s": in older streams).
            j = line.find('{"p":')
            if j < 0:
                j = line.find('{"s":')
            if j < 0:
                continue
            payload = line[j:].strip()
        end = payload.rfind("}")          # trim any trailing log noise after the envelope
        if end >= 0:
            payload = payload[:end + 1]
        try:
            env = json.loads(payload)
            seq, (i, n), frag = env["s"], env["c"], env["d"]
            proc = env.get("p")   # absent in pre-0.2 streams
        except (ValueError, KeyError, TypeError):
            continue  # malformed / truncated chunk — skip, seq gap will show it

        key = (proc, seq)
        slot = pending.setdefault(key, {"n": n, "parts": {}})
        slot["parts"][i] = frag
        if len(slot["parts"]) < slot["n"]:
            continue

        # all chunks present -> reassemble
        b64 = "".join(slot["parts"][k] for k in sorted(slot["parts"]))
        del pending[key]
        try:
            inner = base64.b64decode(b64).decode("utf-8")
        except Exception:
            continue
        if args.raw:
            print(inner, flush=True)
        else:
            rec = json.loads(inner)
            rec["seq"] = seq
            if proc is not None:
                rec["proc"] = proc
            print(json.dumps(rec, ensure_ascii=False), flush=True)

    return 0

if __name__ == "__main__":
    sys.exit(main())
