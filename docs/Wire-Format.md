# Wire Format

[← API Reference](API-Reference.md) · [Wiki home](README.md) · Next: [Platform Support →](Platform-Support.md)

How a `Record` becomes bytes on the console, and how to turn it back. Engine: [`ChunkedConsoleSink`](../src/Uriel/ChunkedConsoleSink.cs).

---

## One line per chunk

```
@@URIEL@@ {"s":1042,"c":[1,3],"d":"<base64 fragment of inner json>"}
└───┬───┘ └┬┘      └─┬─┘ └─┬─┘
 sentinel  s         c     d
 (ASCII    seq #   chunk    base64 fragment of the inner JSON
  grep     (per    [index,  (concat all fragments of one seq, then b64-decode)
  marker)  record) total]
```

| Envelope field | Meaning |
|---|---|
| `s` | sequence number — unique per **record**, shared across its chunks |
| `c` | `[i, n]` — this is chunk **i of n** |
| `d` | base64 fragment of the inner JSON |

### Why this shape
| Choice | Reason |
|---|---|
| **Chunked** ≤ `MaxChunkBytes` (800) | iOS `os_log` truncates/corrupts long lines |
| **base64** payload | keeps fragments ASCII-safe (logcat-proof), avoids nested-escape pain |
| **ASCII sentinel** `@@URIEL@@` | `os_log` mangles multibyte UTF-8 on long lines → would break `grep` |
| **One bg thread** does all of it | hot path only enqueues; serialization never touches the caller |

---

## The inner record (after reassembly)

Hand-rolled JSON — no serializer reflection, deterministic field order, minimal alloc.

```json
{"t":1719400000123,"k":"counter","n":"alloc-rate-bps","v":20480,"sid":"my-run","x":"manual"}
```

| Field | Always? | Meaning |
|---|---|---|
| `t` | ✅ | unix epoch **ms** |
| `k` | ✅ | kind — `meta` / `counter` / `stack` / `exception` / `hang` / `gc` |
| `n` | ✅ | name / label (interned) |
| `v` | ✅ | numeric value (`0` when N/A), round-trip `"R"` format |
| `sid` | only if `SessionId` set | session id |
| `x` | only if present | text / message / stack |

> `parse.py` adds a `seq` field (= envelope `s`) to each emitted record so you can spot loss.

---

## Reassembly algorithm

```
for each line:
    find sentinel (or fallback: locate `{"s":` directly)   ← os_log-mangle recovery
    parse envelope → s, [i,n], d
    buffer[s].parts[i] = d
    if buffer[s] has all n parts:
        b64   = concat parts in order 1..n
        inner = base64decode(b64) as utf-8
        emit inner (+ seq=s)
        drop buffer[s]
```

That's exactly what [`uriel_parse.py`](../tools/uriel_parse.py) does. Truncated/malformed chunks are skipped; the seq gap reveals the loss.

---

## Multi-chunk example (a split stack record)

The desktop harness sets `MaxChunkBytes = 220` *on purpose* to force splits. One stack record `s:30` arrived as 5 chunks and rejoined with no gap:

```
@@URIEL@@ {"s":30,"c":[1,5],"d":"eyJ0Ijox..."}
@@URIEL@@ {"s":30,"c":[2,5],"d":"NzE5NDAw..."}
@@URIEL@@ {"s":30,"c":[3,5],"d":"MDEyMywi..."}
@@URIEL@@ {"s":30,"c":[4,5],"d":"ayI6InN0..."}
@@URIEL@@ {"s":30,"c":[5,5],"d":"YWNrIn0="}
                                   │
                  group by s=30, order by c[0], concat d, b64-decode
                                   ▼
{"t":1719400000123,"k":"stack","n":"round-3","v":0,"x":"   at ...","seq":30}
```

> **Integrity, measured:** the [test report](../TEST_REPORT.md) reassembled iOS (368 chunks→366 records) and Android (297→294) with **0 missing seqs**. The chunk-count > record-count gap is exactly the multi-chunk records rejoining.

---

## Size budget (how chunk count is computed)

```
env    = Sentinel.Length + 40          // ' {"s":N,"c":[i,n],"d":""}' slack
budget = max(64, MaxChunkBytes - env)  // bytes of base64 per chunk
n      = ceil(b64.Length / budget)     // number of chunks
```

So with the default 800 and the 9-char sentinel, each chunk carries ~751 base64 chars.

---

## Writing your own consumer

You don't need `uriel_parse.py` — any language can do it. Minimum viable consumer:

1. Read lines, keep those containing your `Sentinel` (or fall back to `{"s":`).
2. JSON-parse the envelope after the sentinel; trim trailing log noise after the last `}`.
3. Bucket fragments by `s`; once you have all `n`, concat by `c[0]`, base64-decode.
4. The result is the inner record JSON above.

---

Next: [Platform Support →](Platform-Support.md) — what's available where, and why.
