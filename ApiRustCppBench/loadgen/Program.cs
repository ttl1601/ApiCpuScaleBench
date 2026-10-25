using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
try
{
    var command = args.FirstOrDefault() ?? "smoke";
    var flags = new Dictionary<string, string>();
    for (var index = 1; index < args.Length; index += 2)
    {
        if (index + 1 == args.Length) throw new ArgumentException("Missing flag value");
        if (!new[] { "--ccu", "--seconds", "--warmup", "--repetitions", "--output", "--payload" }.Contains(args[index]))
            throw new ArgumentException("Unknown flag");
        flags.Add(args[index], args[index + 1]);
    }
    var ccuLevels = flags.GetValueOrDefault("--ccu", "100,1000,5000").Split(',').Select(int.Parse).ToArray();
    var seconds = int.Parse(flags.GetValueOrDefault("--seconds", "20"));
    var warmup = int.Parse(flags.GetValueOrDefault("--warmup", "5"));
    var repetitions = int.Parse(flags.GetValueOrDefault("--repetitions", "3"));
    if (seconds < 1 || warmup < 0 || repetitions < 1 || ccuLevels.Length == 0 || ccuLevels.Any(value => value is < 1 or > 10000))
        throw new ArgumentException("Invalid load configuration");
    var payloadPath = flags.GetValueOrDefault("--payload", "/payloads/input.json");
    var payload = await File.ReadAllBytesAsync(payloadPath);
    var expected = JsonNode.Parse(payload)!;
    var targets = new[] {
        new Target("cpp", Environment.GetEnvironmentVariable("CPP_URL") ?? "http://cpp:8080"),
        new Target("rust", Environment.GetEnvironmentVariable("RUST_URL") ?? "http://rust:8080")
    };
    using var cancellation = new CancellationTokenSource();
    Console.CancelKeyPress += (_, eventArgs) => { eventArgs.Cancel = true; cancellation.Cancel(); };
    var output = Path.Combine(flags.GetValueOrDefault("--output", "/results"), DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ") + "-" + command);
    Directory.CreateDirectory(output);
    var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
    var smokeResults = new List<object>();
    foreach (var target in targets)
    {
        await target.ReadyAsync(cancellation.Token);
        await target.ValidateAsync(payload, expected, cancellation.Token);
        if (command == "smoke")
        {
            await Smoke.RunAsync(target, payload, expected, cancellation.Token);
            smokeResults.Add(new { target.Name, Passed = true, Cases = Smoke.CaseCount });
        }
    }
    if (command == "smoke")
    {
        await File.WriteAllTextAsync(Path.Combine(output, "smoke.json"), JsonSerializer.Serialize(smokeResults, jsonOptions));
        Console.WriteLine($"PASS: {Smoke.CaseCount} cases per API; semantic round-trip and rejection checks. {output}");
        return 0;
    }
    if (command != "run") throw new ArgumentException("Command: smoke or run");
    await File.WriteAllTextAsync(Path.Combine(output, "metadata.json"), JsonSerializer.Serialize(new {
        StartedUtc = DateTimeOffset.UtcNow, Ccu = ccuLevels, Seconds = seconds, Warmup = warmup, Repetitions = repetitions,
        PayloadBytes = payload.Length, PayloadSha256 = Convert.ToHexString(SHA256.HashData(payload)),
        Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
        ProcessorCount = Environment.ProcessorCount,
        Model = "closed-loop zero think time; sequential targets; reversed order on even repetitions; warmup per scenario",
        Request = "HTTP/1.1 exact, keepalive, no TLS/compression, POST UTF-8 text to typed object and JSON echo",
        Validation = "all responses fully read and status checked; every 1024th successful worker response semantically checked; full functional suite run separately",
        TimeoutSeconds = 15, MaxConnectionsPerServer = 10000, CooldownSeconds = 2,
        Note = "Latency includes client queue/network/body transfer; RPS denominator includes drain. Not pure parser speed."
    }, jsonOptions));
    var results = new List<RunResult>();
    Console.WriteLine("API      CCU REP     Success/s  Error%    p50 ms    p95 ms    p99 ms  Client cores");
    foreach (var ccu in ccuLevels)
    for (var repetition = 1; repetition <= repetitions; repetition++)
    {
        var ordered = repetition % 2 == 1 ? targets : targets.Reverse().ToArray();
        foreach (var target in ordered)
        {
            if (warmup > 0)
            {
                var preparation = await Measure.RunAsync(target, payload, expected, ccu, repetition, warmup, cancellation.Token);
                if (preparation.Errors > 0) Console.WriteLine($"WARNING warmup {target.Name}: {preparation.Errors} errors");
            }
            var result = await Measure.RunAsync(target, payload, expected, ccu, repetition, seconds, cancellation.Token);
            results.Add(result);
            await File.WriteAllTextAsync(Path.Combine(output, "results.json"), JsonSerializer.Serialize(results, jsonOptions));
            await File.WriteAllLinesAsync(Path.Combine(output, "results.csv"), new[] {
                "api,ccu,repetition,success,errors,success_rps,error_percent,p50_ms,p95_ms,p99_ms,peak_inflight,wall_seconds,drain_seconds,client_cpu_cores,response_bytes,sampled_checks"
            }.Concat(results.Select(row => FormattableString.Invariant($"{row.Api},{row.Ccu},{row.Repetition},{row.Success},{row.Errors},{row.SuccessRps:F3},{row.ErrorPercent:F3},{row.AllLatency.P50Ms:F3},{row.AllLatency.P95Ms:F3},{row.AllLatency.P99Ms:F3},{row.PeakInflight},{row.WallSeconds:F3},{row.DrainSeconds:F3},{row.ClientCpuCores:F3},{row.ResponseBytes},{row.SampledChecks}"))));
            Console.WriteLine($"{result.Api,-7} {ccu,5} {repetition,3} {result.SuccessRps,13:F1} {result.ErrorPercent,7:F2} {result.AllLatency.P50Ms,9:F2} {result.AllLatency.P95Ms,9:F2} {result.AllLatency.P99Ms,9:F2} {result.ClientCpuCores,13:F2}");
            await Task.Delay(2000, cancellation.Token);
        }
    }
    Console.WriteLine($"Results: {output}");
    foreach (var target in targets) target.Dispose();
    return results.Any(row => row.Errors > 0 || row.Success == 0) ? 2 : 0;
}
catch (OperationCanceledException) { Console.Error.WriteLine("Cancelled; completed scenarios remain saved."); return 130; }
catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }

internal sealed class Target : IDisposable
{
    public string Name { get; }
    public HttpClient Client { get; }
    public Target(string name, string url)
    {
        Name = name;
        Client = new HttpClient(new SocketsHttpHandler {
            MaxConnectionsPerServer = 10000, AutomaticDecompression = DecompressionMethods.None,
            ConnectTimeout = TimeSpan.FromSeconds(15), PooledConnectionIdleTimeout = TimeSpan.FromMinutes(10)
        }) { BaseAddress = new Uri(url), Timeout = TimeSpan.FromSeconds(15),
            DefaultRequestVersion = HttpVersion.Version11, DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact };
    }
    public async Task ReadyAsync(CancellationToken token)
    {
        var deadline = DateTime.UtcNow.AddSeconds(90);
        while (true)
        {
            try { if (await Client.GetStringAsync("/health", token) == "ok") return; }
            catch (HttpRequestException) { }
            if (DateTime.UtcNow >= deadline) throw new TimeoutException($"{Name} not ready");
            await Task.Delay(500, token);
        }
    }
    public async Task<(int Status, byte[] Body)> SendAsync(byte[] payload, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/parse");
        if (payload.Length > 1_048_576) request.Headers.ExpectContinue = true;
        request.Content = new ByteArrayContent(payload);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("text/plain") { CharSet = "utf-8" };
        using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        return ((int)response.StatusCode, await response.Content.ReadAsByteArrayAsync(token));
    }
    public async Task ValidateAsync(byte[] payload, JsonNode expected, CancellationToken token)
    {
        var result = await SendAsync(payload, token);
        if (result.Status != 200 || !JsonNode.DeepEquals(JsonNode.Parse(result.Body), expected))
            throw new InvalidOperationException($"{Name} round-trip failed, status {result.Status}");
    }
    public void Dispose() => Client.Dispose();
}

internal static class Smoke
{
    public const int CaseCount = 16;
    public static async Task RunAsync(Target target, byte[] payload, JsonNode expected, CancellationToken token)
    {
        await target.ValidateAsync(payload, expected, token);
        var unicode = expected.DeepClone();
        unicode["name"] = "Chào bạn 東京 😀";
        await target.ValidateAsync(Encoding.UTF8.GetBytes(unicode.ToJsonString()), unicode, token);
        var cases = new List<(byte[] Body, int Status)> {
            (Encoding.UTF8.GetBytes("{"), 400), ([], 400), (Encoding.UTF8.GetBytes("{}"), 400),
            (Encoding.UTF8.GetBytes("null"), 400), (Encoding.UTF8.GetBytes("[]"), 400),
            (payload.Concat(Encoding.UTF8.GetBytes(" {} ")).ToArray(), 400),
            (new byte[] { 0xff, 0xfe, 0xff }, 400), (new byte[1_048_577], 413)
        };
        foreach (var (field, value) in new (string, JsonNode?)[] {
            ("name", null), ("tags", null), ("id", JsonValue.Create("wrong")),
            ("unknown", JsonValue.Create(1)), ("items", JsonNode.Parse("[null]")),
            ("attributes", JsonNode.Parse("{\"bad\":null}")) })
        {
            var invalid = expected.DeepClone(); invalid[field] = value;
            cases.Add((Encoding.UTF8.GetBytes(invalid.ToJsonString()), 400));
        }
        foreach (var test in cases)
        {
            var response = await target.SendAsync(test.Body, token);
            if (response.Status != test.Status)
                throw new InvalidOperationException($"{target.Name}: expected {test.Status}, got {response.Status}");
        }
        if (cases.Count + 2 != CaseCount) throw new InvalidOperationException("Smoke accounting mismatch");
        Console.WriteLine($"Smoke {target.Name}: {CaseCount} cases PASS");
    }
}

internal sealed record RunResult(string Api, int Ccu, int Repetition, long Success, long Errors,
    double SuccessRps, double ErrorPercent, Latency AllLatency, Latency SuccessLatency,
    int PeakInflight, double WallSeconds, double DrainSeconds, double ClientCpuCores,
    DateTimeOffset StartedUtc, DateTimeOffset EndedUtc,
    long ClientAllocatedBytes, long ResponseBytes, long SampledChecks, Dictionary<string, long> ErrorTypes);

internal static class Measure
{
    public static async Task<RunResult> RunAsync(Target target, byte[] payload, JsonNode expected,
        int ccu, int repetition, int seconds, CancellationToken token)
    {
        var all = new Histogram(); var successes = new Histogram();
        var errors = new ConcurrentDictionary<string, long>();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = 0; var peak = 0; long responseBytes = 0; long sampled = 0; long deadline = 0;
        var workers = Enumerable.Range(0, ccu).Select(async worker => {
            await gate.Task;
            var iteration = worker;
            while (!token.IsCancellationRequested && Stopwatch.GetTimestamp() < deadline)
            {
                var started = Stopwatch.GetTimestamp();
                var count = Interlocked.Increment(ref active);
                int previous;
                do { previous = Volatile.Read(ref peak); }
                while (count > previous && Interlocked.CompareExchange(ref peak, count, previous) != previous);
                var succeeded = false;
                try
                {
                    var response = await target.SendAsync(payload, token);
                    Interlocked.Add(ref responseBytes, response.Body.Length);
                    if (response.Status != 200) throw new HttpStatusException(response.Status);
                    if ((iteration++ & 1023) == 0)
                    {
                        if (!JsonNode.DeepEquals(JsonNode.Parse(response.Body), expected))
                            throw new InvalidOperationException("Response semantic mismatch");
                        Interlocked.Increment(ref sampled);
                    }
                    succeeded = true;
                }
                catch (Exception exception) when (!token.IsCancellationRequested)
                {
                    var key = exception is HttpStatusException status ? $"HTTP{status.Status}" : exception.GetType().Name;
                    errors.AddOrUpdate(key, 1, (_, count) => count + 1);
                }
                finally
                {
                    var elapsed = Stopwatch.GetTimestamp() - started;
                    Interlocked.Decrement(ref active); all.Record(elapsed);
                    if (succeeded) successes.Record(elapsed);
                }
            }
        }).ToArray();
        using var process = Process.GetCurrentProcess();
        var cpuBefore = process.TotalProcessorTime;
        var allocatedBefore = GC.GetTotalAllocatedBytes();
        var startedUtc = DateTimeOffset.UtcNow;
        var start = Stopwatch.GetTimestamp();
        deadline = start + (long)(seconds * (double)Stopwatch.Frequency);
        gate.SetResult();
        await Task.WhenAll(workers); token.ThrowIfCancellationRequested();
        var wall = Stopwatch.GetElapsedTime(start).TotalSeconds;
        var errorCount = errors.Values.Sum();
        if (all.Count != successes.Count + errorCount) throw new InvalidOperationException("Accounting mismatch");
        return new RunResult(target.Name, ccu, repetition, successes.Count, errorCount, successes.Count / wall,
            errorCount * 100.0 / Math.Max(1, all.Count), all.Snapshot(), successes.Snapshot(), peak, wall,
            Math.Max(0, wall - seconds), (process.TotalProcessorTime - cpuBefore).TotalSeconds / wall,
            startedUtc, DateTimeOffset.UtcNow,
            GC.GetTotalAllocatedBytes() - allocatedBefore, responseBytes, sampled, new(errors));
    }
}

internal sealed class HttpStatusException(int status) : Exception
{
    public int Status { get; } = status;
}
