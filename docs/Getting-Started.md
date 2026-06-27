# Getting Started

[← Overview](Overview.md) · [Wiki home](README.md) · Next: [Configuration →](Configuration.md)

Goal: from zero to **one Uriel record on your screen** in ~5 minutes.

---

## 0. Prerequisites

| Need | Why |
|---|---|
| .NET 9 SDK (`9.0.314`, see [`global.json`](../global.json)) | builds the engine + MAUI heads |
| `dotnet workload install maui` | only for the iOS/Android heads |
| Python 3 | to run `tools/uriel_parse.py` (stdlib only, no pip) |

> The plain `net9.0` target is **workload-free** and console-runnable — good for trying the engine on your desktop first (see step 4).

---

## 1. Add the package

Reference `Uriel.Profiler` (build it locally with `dotnet pack`, see [Build](#build-from-source)).

```xml
<PackageReference Include="Uriel.Profiler" Version="0.1.0" />
```

## 2. Activate — pick ONE

### A. MAUI one-liner (recommended, reliable)
In `MauiProgram.cs`:
```csharp
using Uriel;

var builder = MauiApp.CreateBuilder();
builder.UseMauiApp<App>();

builder.UseUriel(o =>
{
    o.Channels = Channels.All;       // Counters | Exceptions | Hangs | Gc
    o.CounterIntervalSec = 1;
    o.SessionId = "my-run";          // optional, stamps every record
});
```

### B. Manual `Start` (non-MAUI / console / tests)
```csharp
using Uriel;
UrielProfiler.Start(o => o.Channels = Channels.Counters | Channels.Exceptions);
```

### C. Zero-code env toggle (best-effort, field flip)
```bash
URIEL_AUTOSTART=1   # starts with defaults when the assembly loads
```
> ⚠️ Best-effort only — a `[ModuleInitializer]` runs only once *something* in the Uriel assembly is touched. If you reference the package but never call it, it may not load. **Prefer A.** Details: [AutoStart caveat](Troubleshooting.md#env-autostart-didnt-fire).

> `Start` is **idempotent** — only the first call wins, safe to call from anywhere.

## 3. Add on-demand markers (optional, anywhere in your code)

```csharp
UrielProfiler.Mark("frame-time-ms", 16.7);          // custom metric
UrielProfiler.Mark("cart-items", 3, "checkout");     // value + note
UrielProfiler.CaptureStack("before-heavy-parse");    // current managed stack
```

## 4. See a record — fastest path is the desktop harness

No device needed. The [`Harness`](../samples/Harness/Program.cs) drives the engine and prints the raw wire format:

```bash
dotnet run --project samples/Harness -c Release | python3 tools/uriel_parse.py
```

You should see reassembled records, one per line:
```json
{"t":1719400000123,"k":"counter","n":"heap-bytes","v":8557240,"sid":"harness","seq":2}
{"t":1719400000130,"k":"stack","n":"round-0","v":0,"sid":"harness","x":"   at ...","seq":7}
```

If you see those lines, **the engine works**. Now do the same on a device → [Reading the Stream](Reading-the-Stream.md).

---

## What each step gave you

```
 step 2  →  collectors armed, "session-start" meta record emitted
 step 3  →  your own marks/stacks join the same stream
 step 4  →  raw chunks  ──uriel_parse.py──►  clean JSON-L you can pipe to jq/pandas
```

---

## Build from source

```bash
dotnet workload install maui
dotnet pack src/Uriel/Uriel.csproj -c Release      # → Uriel.Profiler.0.1.0.nupkg
```

The project multi-targets `net9.0;net9.0-ios;net9.0-android`. The plain `net9.0` head builds **without** the MAUI workload.

---

Next: [Configuration →](Configuration.md) · or jump to [Channels](Channels.md) to see what you're collecting.
