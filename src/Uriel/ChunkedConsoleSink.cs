using System.Text;
using System.Threading.Channels;

namespace Uriel;

/// <summary>
/// The output engine. Hot path only enqueues (non-blocking, drop-on-full).
/// A single background thread serializes to JSON, base64-chunks oversized lines,
/// and writes to the console. Nothing on the caller's thread blocks or allocates much.
///
/// Wire format, one line per chunk:
///   §URIEL§ {"s":1042,"c":[1,3],"d":"&lt;base64 fragment of inner json&gt;"}
/// Reassemble: group by s, order by c[0], concat d, base64-decode → inner JSON record.
/// Base64 keeps fragments ASCII-safe (logcat-proof) and avoids nested-escaping pain.
/// </summary>
internal sealed class ChunkedConsoleSink : IDisposable
{
    private readonly Channel<Record> _ch;
    private readonly UrielOptions _opt;
    private readonly Task _pump;
    private long _seq;

    public ChunkedConsoleSink(UrielOptions opt)
    {
        _opt = opt;
        _ch = Channel.CreateBounded<Record>(new BoundedChannelOptions(opt.QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.DropWrite,   // never block the app; we'd rather lose a sample
            SingleReader = true,
            SingleWriter = false,
        });
        _pump = Task.Factory.StartNew(PumpAsync, TaskCreationOptions.LongRunning).Unwrap();
    }

    /// <summary>Hot-path entry. Returns immediately; record is dropped if the queue is saturated.</summary>
    public void Enqueue(in Record r) => _ch.Writer.TryWrite(r);

    public void Complete() => _ch.Writer.TryComplete();

    private async Task PumpAsync()
    {
        var sb = new StringBuilder(256);              // reused across records, bg thread only
        var reader = _ch.Reader;
        var stdout = Console.Out;

        while (await reader.WaitToReadAsync().ConfigureAwait(false))
        {
            while (reader.TryRead(out var rec))
            {
                try { WriteRecord(stdout, sb, rec); }
                catch { /* a profiler must never crash its host */ }
            }
        }
    }

    private void WriteRecord(TextWriter o, StringBuilder sb, in Record rec)
    {
        long seq = Interlocked.Increment(ref _seq);
        string inner = BuildInnerJson(sb, rec);
        byte[] raw = Encoding.UTF8.GetBytes(inner);
        string b64 = Convert.ToBase64String(raw);

        // budget for the base64 payload after the envelope overhead
        int env = _opt.Sentinel.Length + 40;          // " {\"s\":N,\"c\":[i,n],\"d\":\"\"}" slack
        int budget = Math.Max(64, _opt.MaxChunkBytes - env);
        int n = (b64.Length + budget - 1) / budget;
        if (n <= 0) n = 1;

        for (int i = 0; i < n; i++)
        {
            int start = i * budget;
            int len = Math.Min(budget, b64.Length - start);
            string frag = b64.Substring(start, len);
            o.WriteLine($"{_opt.Sentinel} {{\"s\":{seq},\"c\":[{i + 1},{n}],\"d\":\"{frag}\"}}");
        }
    }

    /// <summary>Hand-rolled JSON: no serializer reflection, deterministic field order, minimal alloc.</summary>
    private string BuildInnerJson(StringBuilder sb, in Record rec)
    {
        sb.Clear();
        sb.Append("{\"t\":").Append(rec.T)
          .Append(",\"k\":\"").Append(KindTag(rec.Kind)).Append('"')
          .Append(",\"n\":").AppendJsonString(rec.Name)
          .Append(",\"v\":").Append(rec.Val.ToString("R", System.Globalization.CultureInfo.InvariantCulture));

        if (_opt.SessionId is { } sid)
            sb.Append(",\"sid\":").AppendJsonString(sid);
        if (rec.Text is { } txt)
            sb.Append(",\"x\":").AppendJsonString(txt);

        sb.Append('}');
        return sb.ToString();
    }

    private static string KindTag(RecKind k) => k switch
    {
        RecKind.Counter => "counter",
        RecKind.Gc => "gc",
        RecKind.Exception => "exception",
        RecKind.Hang => "hang",
        RecKind.Stack => "stack",
        _ => "meta",
    };

    public void Dispose()
    {
        Complete();
        try { _pump.Wait(TimeSpan.FromSeconds(2)); } catch { /* best effort flush */ }
    }
}

internal static class JsonStringExt
{
    /// <summary>Minimal RFC-8259 string escaping. Enough for names, messages, stacks.</summary>
    public static StringBuilder AppendJsonString(this StringBuilder sb, string s)
    {
        sb.Append('"');
        foreach (char c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else sb.Append(c);
                    break;
            }
        }
        return sb.Append('"');
    }
}
