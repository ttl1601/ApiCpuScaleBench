using System.Collections.Concurrent;
using System.Diagnostics;

namespace DbReadBench;

internal sealed class Histogram
{
    private readonly long[] buckets = new long[2300];
    private long count;
    private long totalTicks;
    private long maxTicks;
    public long Count => Interlocked.Read(ref count);

    public void Record(long ticks)
    {
        var microseconds = ticks * 1_000_000.0 / Stopwatch.Frequency;
        var index = Math.Clamp((int)Math.Ceiling(Math.Log(Math.Max(1, microseconds)) / Math.Log(1.01)), 0, buckets.Length - 1);
        Interlocked.Increment(ref buckets[index]);
        Interlocked.Increment(ref count);
        Interlocked.Add(ref totalTicks, ticks);
        long previous;
        do { previous = Interlocked.Read(ref maxTicks); }
        while (ticks > previous && Interlocked.CompareExchange(ref maxTicks, ticks, previous) != previous);
    }

    public Latency Snapshot() => new(Count, Count == 0 ? 0 : totalTicks * 1000.0 / Stopwatch.Frequency / Count,
        Percentile(0.50), Percentile(0.95), Percentile(0.99), maxTicks * 1000.0 / Stopwatch.Frequency);

    private double Percentile(double percentile)
    {
        if (Count == 0) return 0;
        var target = (long)Math.Ceiling(Count * percentile);
        long cumulative = 0;
        for (var index = 0; index < buckets.Length; index++)
        {
            cumulative += buckets[index];
            if (cumulative >= target) return Math.Min(Math.Pow(1.01, index) / 1000, maxTicks * 1000.0 / Stopwatch.Frequency);
        }
        throw new InvalidOperationException("Histogram count mismatch.");
    }
}

internal sealed record Latency(long Count, double MeanMs, double P50Ms, double P95Ms, double P99Ms, double MaxMs);
internal sealed record Result(string Database, int Ccu, int Repetition, int MaxInflight, int PeakInflight,
    double AdmissionSeconds, double WallSeconds, double DrainSeconds, long Success, long Errors,
    double SuccessRps, double ErrorPercent, Latency AllLatency, Latency SuccessLatency, Latency QueueLatency,
    Latency ServiceLatency, Dictionary<string, long> ErrorTypes, double ClientCpuSeconds,
    long ClientAllocatedBytes, int Gen0Collections, int Gen1Collections, int Gen2Collections,
    int? DriverPeakInflight, double? DriverMeanInflight, int? DriverPeakConnections, long DriverSamples);

internal static class Benchmark
{
    internal const int DriverSamplingIntervalMs = 20;
    public static async Task<Result> RunAsync(IReadStore store, Options options, int ccu, int repetition,
        int seconds, CancellationToken cancellationToken)
    {
        using var limiter = new SemaphoreSlim(options.MaxInflight);
        var all = new Histogram();
        var successes = new Histogram();
        var queue = new Histogram();
        var service = new Histogram();
        var errors = new ConcurrentDictionary<string, long>();
        var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var startTicks = 0L;
        var deadline = 0L;
        var inflight = 0;
        var peak = 0;
        var driverPeak = 0;
        var driverConnections = 0;
        long driverTotal = 0;
        long driverSamples = 0;
        var workers = Enumerable.Range(0, ccu).Select(async worker =>
        {
            var random = new Random(unchecked(options.Seed + worker * 7919));
            await startGate.Task;
            while (!cancellationToken.IsCancellationRequested && Stopwatch.GetTimestamp() < deadline)
            {
                var started = Stopwatch.GetTimestamp();
                var acquired = false;
                var serviceStart = 0L;
                var succeeded = false;
                try
                {
                    acquired = await limiter.WaitAsync(TimeSpan.FromSeconds(options.TimeoutSeconds), cancellationToken);
                    queue.Record(Stopwatch.GetTimestamp() - started);
                    if (!acquired) throw new TimeoutException("Application queue timeout.");
                    var active = Interlocked.Increment(ref inflight);
                    int previous;
                    do { previous = Volatile.Read(ref peak); }
                    while (active > previous && Interlocked.CompareExchange(ref peak, active, previous) != previous);
                    serviceStart = Stopwatch.GetTimestamp();
                    await store.ReadAsync(random.Next(options.Rows) + 1, cancellationToken);
                    succeeded = true;
                }
                catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                {
                    errors.AddOrUpdate(exception.GetType().Name, 1, (_, count) => count + 1);
                }
                finally
                {
                    var finished = Stopwatch.GetTimestamp();
                    if (acquired)
                    {
                        service.Record(finished - serviceStart);
                        Interlocked.Decrement(ref inflight);
                        limiter.Release();
                    }
                    all.Record(finished - started);
                    if (succeeded) successes.Record(finished - started);
                }
            }
        }).ToArray();
        using var process = Process.GetCurrentProcess();
        var cpuBefore = process.TotalProcessorTime;
        var allocatedBefore = GC.GetTotalAllocatedBytes();
        var collectionsBefore = Enumerable.Range(0, 3).Select(GC.CollectionCount).ToArray();
        using var samplingCancellation = new CancellationTokenSource();
        var samplingTask = Task.Run(async () =>
        {
            await startGate.Task;
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(DriverSamplingIntervalMs));
            try
            {
                while (await timer.WaitForNextTickAsync(samplingCancellation.Token))
                {
                    if (store.GetDriverState() is not { } state) continue;
                    driverPeak = Math.Max(driverPeak, state.Inflight);
                    driverConnections = Math.Max(driverConnections, state.OpenConnections);
                    driverTotal += state.Inflight;
                    driverSamples++;
                }
            }
            catch (OperationCanceledException) when (samplingCancellation.IsCancellationRequested) { }
        });
        startTicks = Stopwatch.GetTimestamp();
        deadline = startTicks + (long)(seconds * (double)Stopwatch.Frequency);
        startGate.SetResult();
        try { await Task.WhenAll(workers); }
        finally
        {
            samplingCancellation.Cancel();
            await samplingTask;
        }
        cancellationToken.ThrowIfCancellationRequested();
        var wall = Stopwatch.GetElapsedTime(startTicks).TotalSeconds;
        var errorCount = errors.Values.Sum();
        return new Result(store.Name, ccu, repetition, options.MaxInflight, peak, seconds, wall,
            Math.Max(0, wall - seconds), successes.Count, errorCount, successes.Count / wall,
            all.Count == 0 ? 0 : errorCount * 100.0 / all.Count,
            all.Snapshot(), successes.Snapshot(), queue.Snapshot(), service.Snapshot(), new(errors),
            (process.TotalProcessorTime - cpuBefore).TotalSeconds, GC.GetTotalAllocatedBytes() - allocatedBefore,
            GC.CollectionCount(0) - collectionsBefore[0], GC.CollectionCount(1) - collectionsBefore[1],
            GC.CollectionCount(2) - collectionsBefore[2], driverSamples == 0 ? null : driverPeak,
            driverSamples == 0 ? null : driverTotal / (double)driverSamples,
            driverSamples == 0 ? null : driverConnections, driverSamples);
    }
}
