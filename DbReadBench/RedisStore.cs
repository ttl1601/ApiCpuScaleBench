using StackExchange.Redis;

namespace DbReadBench;

internal sealed class RedisStore(Options options) : IReadStore
{
    private ConnectionMultiplexer connection = null!;
    private IDatabase database = null!;
    public string Name => "redis";

    public async Task InitializeAsync(bool seed, Options settings, CancellationToken cancellationToken)
    {
        var configuration = ConfigurationOptions.Parse(
            Environment.GetEnvironmentVariable("REDIS_CONNECTION") ?? "localhost:16379");
        configuration.AbortOnConnectFail = true;
        configuration.AsyncTimeout = options.TimeoutSeconds * 1000;
        configuration.ConnectTimeout = options.TimeoutSeconds * 1000;
        configuration.BacklogPolicy = BacklogPolicy.FailFast;
        connection = await ConnectionMultiplexer.ConnectAsync(configuration);
        database = connection.GetDatabase();
        var payload = new string('x', settings.PayloadBytes);
        await Parallel.ForEachAsync(Enumerable.Range(1, settings.Rows),
            new ParallelOptions { MaxDegreeOfParallelism = 128, CancellationToken = cancellationToken },
            async (id, token) =>
            {
                token.ThrowIfCancellationRequested();
                if (seed && !await database.StringSetAsync($"bench:read_items:{id}", payload))
                    throw new InvalidOperationException("Redis SET failed.");
                await ReadAsync(id, token);
            });
    }

    public async Task ReadAsync(int id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var value = await database.StringGetAsync($"bench:read_items:{id}");
        if (value.IsNull || ((string?)value)?.Length != options.PayloadBytes)
            throw new InvalidOperationException("Missing Redis key or unexpected payload.");
    }

    public async ValueTask DisposeAsync()
    {
        if (connection is not null) await connection.DisposeAsync();
    }
}
