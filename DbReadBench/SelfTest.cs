using System.Diagnostics;

namespace DbReadBench;

internal static class SelfTest
{
    public static async Task<int> RunAsync()
    {
        var histogram = new Histogram();
        Require(Options.Parse(["run", "--database", "redis,scylla", "--max-inflight", "5000"]).Databases.SequenceEqual(new[] { "redis", "scylla" }), "Redis high inflight selection");
        for (var index = 0; index < 1000; index++) histogram.Record(Stopwatch.Frequency / 1000);
        var latency = histogram.Snapshot();
        Require(latency.Count == 1000 && Math.Abs(latency.P99Ms - 1) < 0.02, "Histogram percentile");
        Require(new Histogram().Snapshot().P99Ms == 0, "Empty histogram");
        Require(Options.Parse(["run", "--database", "cassandra,scylla", "--max-inflight", "5000"]).MaxInflight == 5000, "CQL high inflight option");
        Require(CqlStore.ConnectionCount(256) == 2 && CqlStore.ConnectionCount(5000) == 4 &&
            CqlStore.ConnectionCount(10000) * 2048 > 10000, "CQL pool capacity and headroom");
        Require(Options.Parse(["run", "--database", "cassandra,scylla"]).Databases.SequenceEqual(new[] { "cassandra", "scylla" }), "Cassandra pair selection");
        Require(Options.Parse(["run", "--database", "mongodb,scylla"]).Databases.SequenceEqual(new[] { "mongodb", "scylla" }), "MongoDB pair selection");
        Require(Options.Parse(["run", "--database", "both"]).Databases.SequenceEqual(new[] { "postgres", "scylla" }), "Legacy pair selection");
        Require(Options.Parse(["run"]).Ccu.SequenceEqual(new[] { 100, 1000, 5000, 10000 }), "Default CCU");
        foreach (var invalid in new[] { new[] { "run", "--ccu", "0" }, new[] { "run", "--duration" }, new[] { "run", "--unknown", "1" }, new[] { "run", "--database", "mongodb,unknown" }, new[] { "run", "--database", "mongodb," } })
        {
            var rejected = false;
            try { Options.Parse(invalid); } catch (ArgumentException) { rejected = true; }
            Require(rejected, "Invalid options rejected");
        }
        var options = new Options { MaxInflight = 4, Rows = 100, DurationSeconds = 1 };
        foreach (var invalid in new[] {
            new[] { "run", "--database", "both", "--max-inflight", "5000" },
            new[] { "run", "--database", "mongodb", "--max-inflight", "5000" },
            new[] { "run", "--database", "cassandra", "--max-inflight", "10001" } })
        {
            var rejected = false;
            try { Options.Parse(invalid); } catch (ArgumentException) { rejected = true; }
            Require(rejected, "Unsafe inflight options rejected");
        }
        await using var store = new FakeStore();
        var result = await Benchmark.RunAsync(store, options, 32, 1, 1, CancellationToken.None);
        Require(result.PeakInflight <= 4 && result.PeakInflight > 0, "Inflight bound");
        Require(result.DriverSamples > 0 && result.DriverPeakInflight is > 0 and <= 4, "Driver inflight sampling");
        Require(result.Errors > 0 && result.Success > 0, "Success and error accounting");
        Require(result.AllLatency.Count == result.Success + result.Errors, "Attempt conservation");
        Require(result.ServiceLatency.Count == result.AllLatency.Count, "Service accounting");
        Require(result.QueueLatency.Count == result.AllLatency.Count, "Queue accounting");
        Require(result.WallSeconds >= 1 && result.DrainSeconds >= 0, "Drain accounting");
        Require(Math.Abs(result.SuccessRps - result.Success / result.WallSeconds) < 0.001, "RPS denominator");
        var high = await Benchmark.RunAsync(store, options with { MaxInflight = 5000 }, 5000, 1, 1, CancellationToken.None);
        Require(high.PeakInflight is > 256 and <= 5000, "High inflight is not capped at 256");
        Require(high.AllLatency.Count == high.Success + high.Errors, "High inflight accounting");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var cancelled = false;
        try { await Benchmark.RunAsync(store, options, 32, 1, 5, cancellation.Token); }
        catch (OperationCanceledException) { cancelled = true; }
        Require(cancelled, "Cancellation");
        Console.WriteLine("PASS: histogram, CLI validation, inflight bound, accounting, drain, RPS, cancellation.");
        return 0;
    }

    private static void Require(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException($"Self-test failed: {name}");
    }

    private sealed class FakeStore : IReadStore
    {
        private int active;
        public DriverStateSnapshot? GetDriverState() => new(Volatile.Read(ref active), 1);
        public string Name => "fake";
        public Task InitializeAsync(bool seed, Options options, CancellationToken cancellationToken) => Task.CompletedTask;
        public async Task ReadAsync(int id, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref active);
            try
            {
                await Task.Delay(2, cancellationToken);
                if (id % 10 == 0) throw new InvalidOperationException("Synthetic failure");
            }
            finally { Interlocked.Decrement(ref active); }
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
