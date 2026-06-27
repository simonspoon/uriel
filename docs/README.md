# Uriel.Profiler — Wiki

> Drop-in, **release-safe** runtime profiler for .NET MAUI (iOS + Android).
> Streams chunked **JSON-L** telemetry out the console. No debugger, no `#if DEBUG`, near-zero overhead.

```
  your app  ──►  Uriel collectors  ──►  bounded channel  ──►  bg thread  ──►  console
 (hot path)      counters/hooks       (drop-on-full)        json+base64     os_log / logcat
   enqueue only ───────────────────────────────────────────────────────────────►  grep + parse
```

**One line to turn on:**
```csharp
builder.UseUriel(o => { o.Channels = Channels.All; o.CounterIntervalSec = 1; });
```

---

## 📖 Read in order (new here)

| # | Page | What it gives you |
|---|------|-------------------|
| 1 | [Overview](Overview.md) | What Uriel is, why it exists, how it's wired |
| 2 | [Getting Started](Getting-Started.md) | Install → activate → first record on screen |
| 3 | [Configuration](Configuration.md) | Every `UrielOptions` knob, defaults, when to change |
| 4 | [Channels](Channels.md) | The 4 collectors + full counter catalog |
| 5 | [Reading the Stream](Reading-the-Stream.md) | Capture console on iOS / Android / desktop |

## 🔍 Reference (look up as needed)

| Page | Use when |
|------|----------|
| [API Reference](API-Reference.md) | You need a method signature |
| [Wire Format](Wire-Format.md) | You're writing your own parser |
| [Platform Support](Platform-Support.md) | "Why is counter X missing on iOS?" |
| [Performance](Performance.md) | "Is this safe to ship in release?" |
| [Demo & Validation](Demo-and-Validation.md) | Run the sample app / reproduce the test report |
| [Troubleshooting](Troubleshooting.md) | Something's not showing up |
| [Glossary](Glossary.md) | Unfamiliar term |

---

## TL;DR — the whole tool in 6 lines

- **Collects:** heap / alloc-rate / GC counts, exceptions (+stack), UI-thread hangs, on-demand stacks & marks.
- **Where:** in-process, in **release**, on **Mono** (iOS+Android) and **CoreCLR** (desktop).
- **How out:** chunked JSON-L on the console — the one channel every platform always has.
- **Cost:** hot path *only enqueues* to a lock-free bounded queue; serialization is on one bg thread.
- **Never crashes the host:** every collector wrapped; on queue-full it **drops the sample, not the app**.
- **Read it:** `grep '@@URIEL@@' | python3 tools/uriel_parse.py` → one JSON record per line → `jq` / pandas.

> Status: **v0.1.0** — validated live end-to-end on iOS **and** Android (Mono). See [Demo & Validation](Demo-and-Validation.md).
