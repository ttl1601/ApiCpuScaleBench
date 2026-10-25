using System.Diagnostics;

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
