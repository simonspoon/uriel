namespace Uriel;

/// <summary>
/// Public entry point. Pull the NuGet, call <see cref="Start"/> (or the MAUI
/// builder extension), and chunked JSON-L telemetry streams to the console.
/// Idempotent and crash-safe: a profiler must never take down its host.
/// </summary>
public static class UrielProfiler
{
    private static readonly object Gate = new();
    private static ChunkedConsoleSink? _sink;
    private static RuntimeCounterListener? _counters;
    private static GcPollSampler? _poll;
    private static EventHooks? _hooks;
    private static GcEventSampler? _gc;
    private static bool _started;

    /// <summary>True once Start has run. Subsequent Start calls are no-ops.</summary>
    public static bool IsRunning => _started;

    /// <summary>Begin profiling. Safe to call from anywhere; only the first call wins.</summary>
    public static void Start(Action<UrielOptions>? configure = null)
    {
        if (_started) return;
        lock (Gate)
        {
            if (_started) return;

            var opt = new UrielOptions();
            configure?.Invoke(opt);

            _sink = new ChunkedConsoleSink(opt);
            Emit(RecKind.Meta, "session-start", 0,
                $"channels={opt.Channels};interval={opt.CounterIntervalSec}s");

            if (opt.Channels.HasFlag(Channels.Counters))
            {
                // poll sampler = reliable everywhere (incl. Mono iOS/Android);
                // EventListener = bonus rich counters where CoreCLR delivers them.
                _poll = new GcPollSampler(_sink, opt.CounterIntervalSec);
                _counters = new RuntimeCounterListener(_sink, opt.CounterIntervalSec);
            }

            if (opt.Channels.HasFlag(Channels.Gc))
                _gc = new GcEventSampler(_sink);

            if (opt.Channels.HasFlag(Channels.Exceptions) || opt.Channels.HasFlag(Channels.Hangs))
                _hooks = new EventHooks(_sink, opt);

            _started = true;
        }
    }

    /// <summary>Capture the current managed stack on demand, tagged with a label.</summary>
    public static void CaptureStack(string label)
    {
        var s = _sink;
        if (s is null) return;
        s.Enqueue(new Record(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            RecKind.Stack, label, 0, Environment.StackTrace));
    }

    /// <summary>Emit an arbitrary marker/metric into the same stream (e.g. frame time, custom counter).</summary>
    public static void Mark(string name, double value = 0, string? note = null)
        => Emit(RecKind.Counter, name, value, note);

    private static void Emit(RecKind kind, string name, double val, string? text)
        => _sink?.Enqueue(new Record(
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), kind, string.Intern(name), val, text));

    /// <summary>Flush and stop. Optional — usually you let it run for the app lifetime.</summary>
    public static void Stop()
    {
        lock (Gate)
        {
            if (!_started) return;
            _counters?.Dispose();
            _poll?.Dispose();
            _hooks?.Dispose();
            _gc?.Dispose();
            _sink?.Dispose();
            _counters = null; _poll = null; _hooks = null; _gc = null; _sink = null;
            _started = false;
        }
    }
}
