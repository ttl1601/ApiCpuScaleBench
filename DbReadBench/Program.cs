using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using DbReadBench;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
try
{
    var options = Options.Parse(args);
    if (options.Command == "help")
    {
        Console.WriteLine("""
        DbReadBench (.NET 10) - PostgreSQL / MongoDB / Cassandra / ScyllaDB / Redis point reads
        Commands: seed | run | self-test | help
        Options (value required):
          --ccu 100,1000,5000,10000   --rows 100000   --payload-bytes 256
          --duration 60   --warmup 15   --cooldown 5   --repetitions 3
          --max-inflight 256   --timeout 30   --seed 42
          --database both|postgres|scylla|mongodb|cassandra|redis|redis,scylla   --output results
        Comma-separated lists select any supported databases.
        both preserves the original PostgreSQL + ScyllaDB comparison.
        max-inflight supports 1..10000 for Redis/Cassandra/Scylla only; PostgreSQL/MongoDB stay capped at 256.
        seed upserts IDs 1..rows; run verifies the dataset, then reads only.
        CCU = closed-loop users, not connections. RPS includes drain time.
        """);
        return 0;
    }
    if (options.Command == "self-test") return await SelfTest.RunAsync();
    using var cancellation = new CancellationTokenSource();
    Console.CancelKeyPress += (_, eventArgs) => { eventArgs.Cancel = true; cancellation.Cancel(); };
    var token = cancellation.Token;
    var names = options.Databases;
    var stores = new List<IReadStore>();
    try
    {
        foreach (var name in names)
        {
            IReadStore store = name switch
            {
                "postgres" => new PostgresStore(options),
                "mongodb" => new MongoStore(options),
                "redis" => new RedisStore(options),
                "cassandra" or "scylla" => new CqlStore(options, name),
                _ => throw new ArgumentException($"Unknown database: {name}")
            };
            stores.Add(store);
            Console.WriteLine($"{options.Command}: initializing/verifying {name}, {options.Rows:N0} rows...");
            await store.InitializeAsync(options.Command == "seed", options, token);
        }
        if (options.Command == "seed") { Console.WriteLine("Seed and verification complete."); return 0; }
        var runDirectory = Path.Combine(Path.GetFullPath(options.Output), DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ") + "-" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(runDirectory);
        var results = new List<Result>();
        var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
        await File.WriteAllTextAsync(Path.Combine(runDirectory, "metadata.json"), JsonSerializer.Serialize(new
        {
            StartedUtc = DateTimeOffset.UtcNow, Options = options, Runtime = RuntimeInformation.FrameworkDescription,
            OS = RuntimeInformation.OSDescription, Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            ProcessorCount = Environment.ProcessorCount,
            Model = "closed-loop, zero think time, uniform random primary-key read; sequential databases; alternating order per repetition",
            Latency = "all attempts include application queue + driver + database; logarithmic percentiles upper bound ~1%; success-only also exported",
            Throughput = "successful attempts / actual wall seconds including drain; admission stops at configured duration",
            Cql = new
            {
                Description = "shared driver, protocol V4, single node, RF=1, consistency ONE, prepared SELECT, no retry policy",
                LocalConnections = CqlStore.ConnectionCount(options.MaxInflight),
                MaxRequestsPerConnection = 2048,
                DriverSamplingIntervalMs = Benchmark.DriverSamplingIntervalMs,
                Sampling = "GetState snapshots, target interval 20ms incl. drain; scheduler may delay samples; not server executing-thread count"
            },
            PostgreSql = "single node, auto-prepared parameterized SELECT, connection pool capped at max-inflight",
            MongoDb = "standalone, primary read preference, local read concern, _id lookup, payload-only projection, retries disabled, pool capped at max-inflight",
            Queue = "same application inflight limit for selected drivers; queue timeout and driver timeout are separate budgets",
            Redis = "standalone GET, shared multiplexer, no client cache; AOF everysec, no eviction; driver telemetry unavailable",
            WarmCache = true
        }, jsonOptions), token);
        Console.WriteLine("DB       CCU    REP  Success/s   Error%   p50(ms)   p95(ms)   p99(ms)   Drain(s)");
        foreach (var ccu in options.Ccu)
        for (var repetition = 1; repetition <= options.Repetitions; repetition++)
        {
            var ordered = repetition % 2 == 1 ? stores : stores.AsEnumerable().Reverse();
            foreach (var store in ordered)
            {
                if (options.WarmupSeconds > 0)
                {
                    var warmup = await Benchmark.RunAsync(store, options, ccu, repetition, options.WarmupSeconds, token);
                    if (warmup.Errors > 0) Console.WriteLine($"WARNING warmup {store.Name}: {warmup.Errors} errors");
                }
                var result = await Benchmark.RunAsync(store, options, ccu, repetition, options.DurationSeconds, token);
                results.Add(result);
                await File.WriteAllTextAsync(Path.Combine(runDirectory, "results.json"), JsonSerializer.Serialize(results, jsonOptions), token);
                await File.WriteAllLinesAsync(Path.Combine(runDirectory, "results.csv"), new[] {
                    "database,ccu,repetition,max_inflight,peak_inflight,success,errors,success_rps,error_percent,p50_ms,p95_ms,p99_ms,mean_ms,queue_p95_ms,service_p95_ms,admission_seconds,wall_seconds,drain_seconds,client_cpu_seconds,client_allocated_bytes,driver_peak_inflight,driver_mean_inflight,driver_peak_connections,driver_samples"
                }.Concat(results.Select(item => FormattableString.Invariant($"{item.Database},{item.Ccu},{item.Repetition},{item.MaxInflight},{item.PeakInflight},{item.Success},{item.Errors},{item.SuccessRps:F3},{item.ErrorPercent:F3},{item.AllLatency.P50Ms:F3},{item.AllLatency.P95Ms:F3},{item.AllLatency.P99Ms:F3},{item.AllLatency.MeanMs:F3},{item.QueueLatency.P95Ms:F3},{item.ServiceLatency.P95Ms:F3},{item.AdmissionSeconds:F3},{item.WallSeconds:F3},{item.DrainSeconds:F3},{item.ClientCpuSeconds:F3},{item.ClientAllocatedBytes},{item.DriverPeakInflight},{item.DriverMeanInflight:F3},{item.DriverPeakConnections},{item.DriverSamples}"))), token);
                Console.WriteLine($"{result.Database,-8} {ccu,6} {repetition,4} {result.SuccessRps,10:F1} {result.ErrorPercent,8:F2} {result.AllLatency.P50Ms,9:F2} {result.AllLatency.P95Ms,9:F2} {result.AllLatency.P99Ms,9:F2} {result.DrainSeconds,10:F2}");
                if (options.CooldownSeconds > 0) await Task.Delay(TimeSpan.FromSeconds(options.CooldownSeconds), token);
            }
        }
        Console.WriteLine($"Results: {runDirectory}");
        return results.Any(result => result.Errors > 0 || result.Success == 0) ? 2 : 0;
    }
    finally
    {
        foreach (var store in stores) await store.DisposeAsync();
    }
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Cancelled. Completed scenarios remain saved; interrupted scenario is discarded.");
    return 130;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception);
    return 1;
}
