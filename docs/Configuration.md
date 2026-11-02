# Configuration

[← Getting Started](Getting-Started.md) · [Wiki home](README.md) · Next: [Channels →](Channels.md)

All config lives in [`UrielOptions`](../src/Uriel/UrielOptions.cs). You set it in the `Start` / `UseUriel` callback. Everything has a sane default — set nothing and it just works.

---

## `Channels` — what runs

`[Flags]` enum. Compose exactly what you pay for.

| Flag | Value | Collector | Cost |
|---|---|---|---|
| `None` | 0 | nothing | — |
| `Counters` | `1<<0` | poll sampler (+ desktop EventCounters) | periodic, very low |
| `Exceptions` | `1<<1` | first-chance + unhandled, with stack | on event |
| `Hangs` | `1<<2` | UI-thread heartbeat watchdog | on event |
| `Gc` | `1<<3` | one record per GC (generation, heap, pause) | per GC |
| **`Default`** | — | `Counters \| Exceptions \| Hangs` | the everyday set |
| **`All`** | — | `Default \| Gc` | everything |

```csharp
o.Channels = Channels.Counters | Channels.Exceptions;   // compose à la carte
o.Channels = Channels.Default;                           // or use a preset
```

> `Gc` is the only one **not** in `Default` — it's opt-in because it writes a record per collection.

---

## Full options reference

| Option | Type | Default | What it does | Tune when |
|---|---|---|---|---|
| `Channels` | `Channels` | `Default` | which collectors run | always — pick your set |
| `CounterIntervalSec` | `double` | `1.0` | poll/EventCounter interval (sec). Higher = cheaper | noisy stream → raise to 2–5; fine detail → lower |
| `HangThresholdMs` | `int` | `250` | UI stall (ms) that counts as a hang | false hangs → raise; want stricter → lower |
| `HangStartupGraceMs` | `int` | `3000` | suppress hang reports this long after `Start` (UI busy booting) | slow cold start → raise |
| `ExceptionStackLimit` | `int` | `5` | first-chance: capture `Environment.StackTrace` for the first N of each type+message (`0` = never) | need stacks for repeats → raise |
| `ExceptionMaxPerSec` | `int` | `20` | first-chance: full records per second per type; the rest become `first-chance-suppressed` counts | exception storm flooding the log → lower |
| `QueueCapacity` | `int` | `8192` | max queued records before **drop-on-full** (reported as `dropped-records`) | very bursty → raise (costs memory) |
| `MaxChunkBytes` | `int` | `800` | max UTF-8 bytes per console line before chunking | see warning below |
| `Sentinel` | `string` | `"@@URIEL@@"` | grep marker prefixed to every line. **Must be ASCII** | rarely — match it in your parser |
| `SessionId` | `string?` | `null` | run id stamped on every record (`sid`) | correlating multiple runs |

---

## The two that bite — read these

### `MaxChunkBytes` (default 800)
Sized for **iOS `os_log`**, the *tightest* console — it truncates/corrupts long lines.

```
 800 B  ◄── default, safe for iOS os_log AND android logcat
~4000 B ◄── logcat's looser limit (you could raise toward this on Android-only)
```

> Don't raise above ~800 if you ever run on iOS. The harness sets it to `220` *on purpose* to force multi-chunk splits for testing. See [Wire Format](Wire-Format.md).

### `Sentinel` (default `@@URIEL@@`) — keep it ASCII
iOS routes `Console` through `os_log`, which **mangles multibyte UTF-8** (e.g. `§` = `C2A7`) on long lines — that would break a literal `grep`. The default is plain ASCII for exactly this reason.

> If you change it, change `--sentinel` in [`uriel_parse.py`](../tools/uriel_parse.py) to match. The parser also has a fallback that finds the `{"p":` (or v0.1.0 `{"s":`) envelope directly if the sentinel gets mangled.

---

## Common recipes

```csharp
// Production-lean: counters + crashes only, cheap interval
o.Channels = Channels.Counters | Channels.Exceptions;
o.CounterIntervalSec = 5;

// Hang hunting: tighter threshold, longer startup grace
o.Channels = Channels.Hangs;
o.HangThresholdMs = 150;
o.HangStartupGraceMs = 5000;

// Everything, correlated across runs
o.Channels = Channels.All;
o.SessionId = $"build-{buildNumber}";

// Android-only, fewer lines (logcat tolerates bigger chunks)
o.MaxChunkBytes = 2000;
```

---

Next: [Channels →](Channels.md) — what each collector actually emits.
