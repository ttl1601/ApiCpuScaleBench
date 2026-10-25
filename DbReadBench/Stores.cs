using Cassandra;
using Npgsql;
using NpgsqlTypes;

namespace DbReadBench;

internal readonly record struct DriverStateSnapshot(int Inflight, int OpenConnections);

internal interface IReadStore : IAsyncDisposable
{
    string Name { get; }
    Task InitializeAsync(bool seed, Options options, CancellationToken cancellationToken);
    Task ReadAsync(int id, CancellationToken cancellationToken);
    DriverStateSnapshot? GetDriverState() => null;
}

internal sealed class PostgresStore(Options options) : IReadStore
{
    private readonly NpgsqlDataSource source = NpgsqlDataSource.Create(new NpgsqlConnectionStringBuilder(
        Environment.GetEnvironmentVariable("PG_CONNECTION") ??
        "Host=localhost;Username=bench;Password=bench-local-only;Database=bench;GSS Encryption Mode=Disable")
    {
        MaxPoolSize = options.MaxInflight,
        Timeout = options.TimeoutSeconds,
        CommandTimeout = options.TimeoutSeconds,
        MaxAutoPrepare = 16,
        AutoPrepareMinUsages = 2
    }.ConnectionString);

    public string Name => "postgres";

    public async Task InitializeAsync(bool seed, Options settings, CancellationToken cancellationToken)
    {
        await using var connection = await source.OpenConnectionAsync(cancellationToken);
        if (seed)
        {
            await using var schema = new NpgsqlCommand(
                "CREATE TABLE IF NOT EXISTS read_items (id int PRIMARY KEY, payload text NOT NULL)", connection);
            await schema.ExecuteNonQueryAsync(cancellationToken);
            await using var insert = new NpgsqlCommand("INSERT INTO read_items SELECT id, repeat('x', $2) " +
                "FROM generate_series(1, $1) AS id ON CONFLICT (id) DO UPDATE SET payload = EXCLUDED.payload", connection);
            insert.Parameters.AddWithValue(settings.Rows);
            insert.Parameters.AddWithValue(settings.PayloadBytes);
            insert.CommandTimeout = 0;
            await insert.ExecuteNonQueryAsync(cancellationToken);
            await using var analyze = new NpgsqlCommand("ANALYZE read_items", connection);
            await analyze.ExecuteNonQueryAsync(cancellationToken);
        }
        await using var verify = new NpgsqlCommand(
            "SELECT count(*) FROM read_items WHERE id BETWEEN 1 AND $1 AND octet_length(payload) = $2", connection);
        verify.Parameters.AddWithValue(settings.Rows);
        verify.Parameters.AddWithValue(settings.PayloadBytes);
        if (Convert.ToInt64(await verify.ExecuteScalarAsync(cancellationToken)) != settings.Rows)
            throw new InvalidOperationException("Postgres dataset mismatch. Run seed with matching rows/payload-bytes.");
    }

    public async Task ReadAsync(int id, CancellationToken cancellationToken)
    {
        await using var connection = await source.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("SELECT payload FROM read_items WHERE id = $1", connection);
        command.Parameters.AddWithValue(NpgsqlDbType.Integer, id);
        var payload = await command.ExecuteScalarAsync(cancellationToken);
        if (payload is not string text || text.Length != options.PayloadBytes)
            throw new InvalidOperationException("Missing row or unexpected payload.");
    }

    public ValueTask DisposeAsync() => source.DisposeAsync();
}

internal sealed class CqlStore(Options options, string database) : IReadStore
{
    private readonly Cluster cluster = Cluster.Builder()
        .AddContactPoint(Environment.GetEnvironmentVariable(database == "cassandra" ? "CASSANDRA_HOST" : "SCYLLA_HOST") ?? "localhost")
        .WithPort(int.Parse(Environment.GetEnvironmentVariable(database == "cassandra" ? "CASSANDRA_PORT" : "SCYLLA_PORT")
            ?? (database == "cassandra" ? "19042" : "9042")))
        .WithMaxProtocolVersion(ProtocolVersion.V4)
        .WithPoolingOptions(new PoolingOptions()
            .SetMaxConnectionsPerHost(HostDistance.Local, ConnectionCount(options.MaxInflight))
            .SetCoreConnectionsPerHost(HostDistance.Local, ConnectionCount(options.MaxInflight))
            .SetMaxRequestsPerConnection(2048))
        .WithQueryOptions(new QueryOptions().SetConsistencyLevel(ConsistencyLevel.One))
        .WithSocketOptions(new SocketOptions().SetReadTimeoutMillis(options.TimeoutSeconds * 1000))
        .WithRetryPolicy(FallthroughRetryPolicy.Instance)
        .Build();
    private ISession session = null!;
    private PreparedStatement read = null!;
    public string Name => database;
    internal static int ConnectionCount(int maxInflight) => Math.Max(2, (maxInflight + 2047) / 2048 + 1);

    public DriverStateSnapshot? GetDriverState()
    {
        var state = session.GetState();
        var hosts = state.GetConnectedHosts().ToArray();
        return new DriverStateSnapshot(hosts.Sum(state.GetInFlightQueries), hosts.Sum(state.GetOpenConnections));
    }

    public async Task InitializeAsync(bool seed, Options settings, CancellationToken cancellationToken)
    {
        session = await cluster.ConnectAsync();
        if (seed)
        {
            await session.ExecuteAsync(new SimpleStatement("CREATE KEYSPACE IF NOT EXISTS bench WITH replication = " +
                "{'class': 'SimpleStrategy', 'replication_factor': 1}"));
            await session.ExecuteAsync(new SimpleStatement(
                "CREATE TABLE IF NOT EXISTS bench.read_items (id int PRIMARY KEY, payload text)"));
            var insert = await session.PrepareAsync("INSERT INTO bench.read_items (id, payload) VALUES (?, ?)");
            var payload = new string('x', settings.PayloadBytes);
            await Parallel.ForEachAsync(Enumerable.Range(1, settings.Rows),
                new ParallelOptions { MaxDegreeOfParallelism = Math.Min(128, settings.MaxInflight), CancellationToken = cancellationToken },
                async (id, token) =>
                {
                    token.ThrowIfCancellationRequested();
                    await session.ExecuteAsync(insert.Bind(id, payload));
                });
        }
        read = await session.PrepareAsync("SELECT payload FROM bench.read_items WHERE id = ?");
        await Parallel.ForEachAsync(Enumerable.Range(1, settings.Rows),
            new ParallelOptions { MaxDegreeOfParallelism = Math.Min(128, settings.MaxInflight), CancellationToken = cancellationToken },
            async (id, token) => await ReadAsync(id, token));
    }

    public async Task ReadAsync(int id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var statement = read.Bind(id).SetReadTimeoutMillis(options.TimeoutSeconds * 1000);
        var result = await session.ExecuteAsync(statement);
        var row = result.FirstOrDefault();
        if (row is null || row.GetValue<string>("payload")?.Length != options.PayloadBytes)
            throw new InvalidOperationException("Missing row or unexpected payload.");
    }

    public async ValueTask DisposeAsync() => await cluster.ShutdownAsync();
}
