using System.Diagnostics.Tracing;

namespace Uriel;

/// <summary>
/// Subscribes to the runtime's built-in EventCounters. This is the whole trick:
/// "System.Runtime" already publishes cpu-usage, working-set, gc-heap-size,
/// gen-N-size, alloc-rate, gc-count, threadpool depth, exception-count, lock
/// contention — periodically, in-process, with no debugger and negligible cost.
/// We just listen and forward each sample as a Counter record.
/// </summary>
internal sealed class RuntimeCounterListener : EventListener
{
    // CRITICAL: the EventListener base ctor invokes OnEventSourceCreated for every
    // already-existing source BEFORE this derived ctor body runs — so _sink and
    // _intervalSec are still null/0 at that point. Enabling there would arm the
    // counters with interval "0" and they'd never emit. So we only STASH sources
    // during construction, then enable them once our fields are real.
    private ChunkedConsoleSink _sink = null!;
    private double _intervalSec;
    private List<EventSource>? _pending;
    private bool _ready;

    public RuntimeCounterListener(ChunkedConsoleSink sink, double intervalSec)
    {
        _sink = sink;
        _intervalSec = intervalSec <= 0 ? 1.0 : intervalSec;
        _ready = true;

        // drain anything the base ctor handed us before we were ready
        var pending = _pending;
        _pending = null;
        if (pending is not null)
            foreach (var s in pending)
                Enable(s);
    }

    protected override void OnEventSourceCreated(EventSource source)
    {
        if (source.Name != "System.Runtime") return;
        if (_ready) Enable(source);
        else (_pending ??= new()).Add(source);   // fields not set yet; defer
    }

    private void Enable(EventSource source) =>
        EnableEvents(source, EventLevel.Informational, EventKeywords.All, new Dictionary<string, string?>
        {
            ["EventCounterIntervalSec"] = _intervalSec.ToString(System.Globalization.CultureInfo.InvariantCulture),
        });

    protected override void OnEventWritten(EventWrittenEventArgs e)
    {
        if (e.EventName != "EventCounters" || e.Payload is null || e.Payload.Count == 0) return;
        if (e.Payload[0] is not IDictionary<string, object> p) return;

        string name = p.TryGetValue("Name", out var n) ? n?.ToString() ?? "?" : "?";

        // Mean counters report "Mean"; incrementing counters report "Increment".
        double val =
            p.TryGetValue("Mean", out var m) && m is not null ? ToD(m) :
            p.TryGetValue("Increment", out var i) && i is not null ? ToD(i) :
            0d;

        long t = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _sink.Enqueue(new Record(t, RecKind.Counter, string.Intern(name), val));
    }

    private static double ToD(object o) =>
        o is IConvertible c ? c.ToDouble(System.Globalization.CultureInfo.InvariantCulture) : 0d;
}
