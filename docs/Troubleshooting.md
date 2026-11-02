# Troubleshooting

[← Demo & Validation](Demo-and-Validation.md) · [Wiki home](README.md) · Next: [Glossary →](Glossary.md)

Symptom → cause → fix. Most "missing data" issues are **documented platform limits**, not bugs — see [Platform Support](Platform-Support.md).

---

## Nothing in the console at all

| Check | Fix |
|---|---|
| Did `Start` / `UseUriel` actually run? | `UrielProfiler.IsRunning` should be `true` |
| Grepping the right sentinel? | default is `@@URIEL@@`; match your `Sentinel` and `--sentinel` |
| Capturing the right stream? | iOS=`--console-pty`, Android=`adb logcat` tag `DOTNET`, desktop=stdout. [How](Reading-the-Stream.md) |
| Using `log stream` on iOS? | **Don't** — it drops under load. Use `--console-pty`. |

---

## Env autostart didn't fire

`URIEL_AUTOSTART=1` is **best-effort**. A `[ModuleInitializer]` only runs once *something* in the Uriel assembly is loaded/touched. If you reference the package but never call into it, the module may never initialize.

✅ **Fix:** use `builder.UseUriel(...)` (or any `UrielProfiler.*` call), or set `<UrielAutoStart>true</UrielAutoStart>` in the app's project file to have the build inject the call. Those are the reliable activations. ([AutoStart source](../src/Uriel/AutoStart.cs))

---

## No rich counters (cpu, jit, threadpool…) on device

**Expected.** Those come from `RuntimeCounterListener` (EventCounters), which **only works on CoreCLR**. iOS+Android run Mono → EventCounters never arrive in-process → that source is silently empty. The poll sampler (`heap-bytes`, `alloc-rate-bps`, gen counts…) carries the load. [Why](Platform-Support.md).

> Want the rich set? Run the [desktop harness](Demo-and-Validation.md#desktop-harness--fastest-verification).

---

## `proc-working-set` missing on iOS

**Expected.** `Environment.WorkingSet` returns `0` on Mono/iOS, so Uriel omits the counter rather than emit a fake `0`. Android **does** report it (~163 MB). Native RSS via P/Invoke is [roadmap](Platform-Support.md#roadmap).

---

## Exception records have a message but no stack (Mono)

Under Mono a first-chance exception has a **null `StackTrace`**, so Uriel captures `Environment.StackTrace` (which includes the throw site) for the first `ExceptionStackLimit` (default 5) of each type+message. After that you get the message only. Raise `ExceptionStackLimit` if you need more, or use `CaptureStack()` at the catch site.

---

## Counters never emit even on desktop

If you ever see EventCounters armed with interval `0`, they won't fire. Uriel guards against this: the `EventListener` base ctor fires `OnEventSourceCreated` **before** the derived ctor sets its fields, so sources are **stashed** and enabled only once fields are real. ([Why, in source](../src/Uriel/RuntimeCounterListener.cs)) — if you fork the listener, preserve that deferral.

---

## Hang watchdog fires during startup

**Expected during cold start** — the UI thread is busy booting. That's what `HangStartupGraceMs` (default 3000) suppresses. Slow cold start? Raise it. False positives later? Raise `HangThresholdMs`. [Config](Configuration.md).

> On **desktop** the watchdog is intentionally silent (no MAUI dispatcher, `#if HAS_MAUI`).

---

## Android app crashes on launch (SIGABRT)

```
No assemblies found in '...'. Fast Deployment ...
```
A bare `adb install` of a Debug **fast-deploy** APK has no embedded assemblies.

✅ **Fix:** build self-contained:
```bash
dotnet build samples/UrielDemo/UrielDemo.csproj -f net9.0-android -c Debug -p:EmbedAssembliesIntoApk=true
# or
dotnet build ... -t:Install
```

---

## Android qorvex automation fails to build its agent

The qorvex UIAutomator agent builds with Gradle; **AGP 8.x requires JDK 17**.

✅ **Fix:** in the **same shell that starts the qorvex server**:
```bash
export JAVA_HOME=/usr/local/opt/openjdk@17
export QORVEX_ANDROID_JAVA_HOME=$JAVA_HOME
```
If the server already started with an older JDK: `qorvex -s <session> stop`, then restart. (Tracked in repo memory `android-qorvex-testing.md`.)

---

## Lines look garbled / sentinel missing on iOS

`os_log` mangles multibyte UTF-8 on long lines. That's why the sentinel is **ASCII** and payloads are **base64**. If you changed `Sentinel` to something non-ASCII, revert it. The parser also falls back to locating the `{"p":` (or v0.1.0 `{"s":`) envelope directly when the sentinel is mangled. [Wire Format](Wire-Format.md).

---

## Losing samples under heavy load

By design: the queue is **drop-on-full** — it loses a sample before stalling your app. Uriel reports it: a `meta` record `dropped-records` (`v` = records lost since the last report, at most once a second).

✅ **Fix:** raise `QueueCapacity` (costs memory) and/or raise `CounterIntervalSec` to cut volume. [Performance](Performance.md).

---

## Records out of order / interleaved

Chunks from different records can interleave on the wire — that's fine. Reassembly groups by `s` (seq), so order doesn't matter. `uriel_parse.py` handles it. [Wire Format](Wire-Format.md#reassembly-algorithm).

---

Still stuck? Check the [test report](TEST_REPORT.md) for a known-good capture to compare against.

Next: [Glossary →](Glossary.md)
