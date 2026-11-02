# API Reference

[← Reading the Stream](Reading-the-Stream.md) · [Wiki home](README.md) · Next: [Wire Format →](Wire-Format.md)

The whole public surface is small. Namespace: `Uriel`.

---

## `UrielProfiler` (static) — [source](../src/Uriel/Uriel.cs)

The entry point. Idempotent and crash-safe: a profiler must never take down its host.

| Member | Signature | Notes |
|---|---|---|
| `Start` | `void Start(Action<UrielOptions>? configure = null)` | Begin profiling. **Only the first call wins** (idempotent). Safe from anywhere. |
| `Stop` | `void Stop()` | Flush + stop. Optional — usually you run for app lifetime. Drains the channel (≤2s). |
| `Mark` | `void Mark(string name, double value = 0, string? note = null)` | Emit a counter record. `note` → the `x` field. |
| `CaptureStack` | `void CaptureStack(string label)` | Capture the current managed stack (`Environment.StackTrace`), tagged `label`. |
| `IsRunning` | `bool IsRunning { get; }` | `true` once `Start` has run. |

```csharp
UrielProfiler.Start(o => o.Channels = Channels.All);
UrielProfiler.Mark("frame-time-ms", 16.7);
UrielProfiler.Mark("cart-items", 3, "checkout");
UrielProfiler.CaptureStack("before-heavy-parse");
bool on = UrielProfiler.IsRunning;
UrielProfiler.Stop();   // optional
```

### Behavior guarantees
- **Idempotent:** second `Start` is a no-op (double-checked lock).
- **Null-safe before Start:** `Mark` / `CaptureStack` silently no-op if the sink isn't up yet.
- **Never throws from the hot path:** enqueue is `TryWrite` (drop-on-full).

---

## `MauiIntegration` (extension) — [source](../src/Uriel/MauiIntegration.cs)

| Member | Signature |
|---|---|
| `UseUriel` | `MauiAppBuilder UseUriel(this MauiAppBuilder builder, Action<UrielOptions>? configure = null)` |

```csharp
builder.UseUriel(o => { o.Channels = Channels.All; o.CounterIntervalSec = 1; });
```
Just calls `UrielProfiler.Start(configure)` and returns the builder for chaining. Compiles only on the MAUI heads (`net9.0-ios` / `net9.0-android`), not plain `net9.0`.

---

## `UrielOptions` (class) — [source](../src/Uriel/UrielOptions.cs)

Every knob, with defaults, is in **[Configuration](Configuration.md)**. Quick list:

```
Channels Channels            = Channels.Default
double   CounterIntervalSec  = 1.0
int      HangThresholdMs      = 250
int      HangStartupGraceMs   = 3000
int      QueueCapacity        = 8192
int      MaxChunkBytes        = 800
string   Sentinel             = "@@URIEL@@"
string?  SessionId            = null
```

## `Channels` (flags enum) — [source](../src/Uriel/UrielOptions.cs)

`None`, `Counters`, `Exceptions`, `Hangs`, `Gc`, `Default`, `All`. Full table in [Configuration](Configuration.md#channels--what-runs) and [Channels](Channels.md).

---

## `RecKind` / `Record` — [source](../src/Uriel/Record.cs)

The wire model. `Record` is the immutable struct pushed on the hot path.

```csharp
public enum RecKind : byte { Counter, Gc, Exception, Hang, Stack, Meta }

public readonly struct Record
{
    public readonly long    T;     // unix epoch ms
    public readonly RecKind Kind;
    public readonly string  Name;  // interned
    public readonly double  Val;   // 0 when N/A
    public readonly string? Text;  // message / stack; null on hot counters
    public Record(long t, RecKind kind, string name, double val, string? text = null);
}
```
`Record` and `RecKind` are `public` so you *can* build your own sink, but `ChunkedConsoleSink` is `internal` — the console is the supported output. See [Wire Format](Wire-Format.md) for how `Record` becomes bytes.

---

## Zero-code activation — [source](../src/Uriel/AutoStart.cs)

**Reliable:** set `<UrielAutoStart>true</UrielAutoStart>` in the app's project file. The package's `buildTransitive` targets generate a `[ModuleInitializer]` in your app assembly that calls `UrielProfiler.Start()` — defaults only: don't combine it with `UseUriel(configure)`/`Start(configure)` (the first `Start` wins, so your options would be ignored). C# projects only.

**Env var:** Uriel's own `[ModuleInitializer]` checks `URIEL_AUTOSTART`:

| Value | Result |
|---|---|
| `1`, `true`, `TRUE`, `yes`, `on` | `UrielProfiler.Start()` with defaults |
| empty / unset / anything else | no-op |

> Env var alone is best-effort (module init runs only when the Uriel assembly is touched). Prefer `UseUriel` or `UrielAutoStart`. See [Troubleshooting](Troubleshooting.md#env-autostart-didnt-fire).

---

Next: [Wire Format →](Wire-Format.md)
