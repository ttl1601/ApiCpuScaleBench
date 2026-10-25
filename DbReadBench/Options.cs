namespace DbReadBench;

internal sealed record Options
{
    public string Command { get; init; } = "help";
    public int[] Ccu { get; init; } = [100, 1000, 5000, 10000];
    public int Rows { get; init; } = 100_000;
    public int PayloadBytes { get; init; } = 256;
    public int DurationSeconds { get; init; } = 60;
    public int WarmupSeconds { get; init; } = 15;
    public int CooldownSeconds { get; init; } = 5;
    public int Repetitions { get; init; } = 3;
    public int MaxInflight { get; init; } = 256;
    public int TimeoutSeconds { get; init; } = 30;
    public int Seed { get; init; } = 42;
    public string Database { get; init; } = "both";
    public string Output { get; init; } = "results";
    public string[] Databases => Database == "both"
        ? ["postgres", "scylla"]
        : Database.Split(',').Distinct().ToArray();

    public static Options Parse(string[] args)
    {
        var options = new Options { Command = args.FirstOrDefault() ?? "help" };
        for (var index = 1; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length) throw new ArgumentException($"Missing value for {args[index]}");
            var value = args[index + 1];
            options = args[index] switch
            {
                "--ccu" => options with { Ccu = value.Split(',').Select(int.Parse).ToArray() },
                "--rows" => options with { Rows = int.Parse(value) },
                "--payload-bytes" => options with { PayloadBytes = int.Parse(value) },
                "--duration" => options with { DurationSeconds = int.Parse(value) },
                "--warmup" => options with { WarmupSeconds = int.Parse(value) },
                "--cooldown" => options with { CooldownSeconds = int.Parse(value) },
                "--repetitions" => options with { Repetitions = int.Parse(value) },
                "--max-inflight" => options with { MaxInflight = int.Parse(value) },
                "--timeout" => options with { TimeoutSeconds = int.Parse(value) },
                "--seed" => options with { Seed = int.Parse(value) },
                "--database" => options with { Database = value.ToLowerInvariant() },
                "--output" => options with { Output = value },
                _ => throw new ArgumentException($"Unknown option: {args[index]}")
            };
        }
        if (options.Command is not ("help" or "seed" or "run" or "self-test"))
            throw new ArgumentException("Command must be help, seed, run or self-test.");
        if (options.Ccu.Length == 0 || options.Ccu.Any(value => value is < 1 or > 100_000) ||
            options.Rows < 1 || options.PayloadBytes is < 1 or > 1_048_576 ||
            options.DurationSeconds < 1 || options.WarmupSeconds < 0 || options.CooldownSeconds < 0 ||
            options.Repetitions < 1 || options.MaxInflight is < 1 or > 10000 || options.TimeoutSeconds is < 1 or > 600 ||
            options.Databases.Any(name => name is not ("postgres" or "scylla" or "mongodb" or "cassandra" or "redis")))
            throw new ArgumentException("Invalid options. CCU: 1..100000; max-inflight: 1..10000; timeout: 1..600; warmup/cooldown >= 0.");
        if (options.MaxInflight > 256 && options.Databases.Any(name => name is not ("cassandra" or "scylla" or "redis")))
            throw new ArgumentException("max-inflight > 256 is supported only for Redis/Cassandra/Scylla; PostgreSQL and MongoDB remain capped at 256.");
        return options;
    }
}
