using Uriel;

// Desktop smoke test for the Uriel engine: counters + exception + on-demand stack.
// Output is the same chunked JSON-L you'd see in logcat / os_log; pipe it to
// tools/uriel_parse.py to verify reassembly.

UrielProfiler.Start(o =>
{
    o.Channels = Channels.Counters | Channels.Exceptions;
    o.CounterIntervalSec = 0.5;          // fast so the test finishes quickly
    o.SessionId = "harness";
    o.MaxChunkBytes = 220;               // tiny on purpose -> forces multi-chunk records
});

UrielProfiler.Mark("harness-begin", 0, "starting workload");

// Generate managed allocations so alloc-rate / gc-heap-size counters move.
var sink = new List<byte[]>();
for (int round = 0; round < 6; round++)
{
    for (int i = 0; i < 2000; i++)
        sink.Add(new byte[1024]);
    sink.Clear();
    GC.Collect();

    UrielProfiler.CaptureStack($"round-{round}");

    try { throw new InvalidOperationException($"synthetic fault {round}"); }
    catch { /* first-chance hook fires regardless */ }

    await Task.Delay(400);
}

UrielProfiler.Mark("harness-end");
await Task.Delay(700);                    // let the last counter sample flush
UrielProfiler.Stop();                     // drains the channel before exit
