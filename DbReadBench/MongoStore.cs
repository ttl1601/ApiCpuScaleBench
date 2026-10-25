using MongoDB.Bson;
using MongoDB.Driver;

namespace DbReadBench;

internal sealed class MongoStore : IReadStore
{
    private readonly Options options;
    private readonly MongoClient client;
    private readonly IMongoCollection<BsonDocument> collection;
    private readonly ProjectionDefinition<BsonDocument> projection =
        Builders<BsonDocument>.Projection.Include("payload").Exclude("_id");

    public MongoStore(Options options)
    {
        this.options = options;
        var settings = MongoClientSettings.FromConnectionString(
            Environment.GetEnvironmentVariable("MONGO_CONNECTION") ?? "mongodb://localhost:27017/?directConnection=true");
        settings.MaxConnectionPoolSize = options.MaxInflight;
        settings.MaxConnecting = 8;
        settings.WaitQueueTimeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        settings.ServerSelectionTimeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        settings.ConnectTimeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        settings.SocketTimeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        settings.RetryReads = false;
        settings.RetryWrites = false;
        settings.ReadPreference = ReadPreference.Primary;
        settings.ReadConcern = ReadConcern.Local;
        client = new MongoClient(settings);
        collection = client.GetDatabase("bench").GetCollection<BsonDocument>("read_items");
    }

    public string Name => "mongodb";

    public async Task InitializeAsync(bool seed, Options settings, CancellationToken cancellationToken)
    {
        if (seed)
        {
            var payload = new string('x', settings.PayloadBytes);
            foreach (var ids in Enumerable.Range(1, settings.Rows).Chunk(1000))
            {
                var writes = ids.Select(id => (WriteModel<BsonDocument>)new ReplaceOneModel<BsonDocument>(
                    Builders<BsonDocument>.Filter.Eq("_id", id),
                    new BsonDocument { { "_id", id }, { "payload", payload } }) { IsUpsert = true });
                await collection.BulkWriteAsync(writes, new BulkWriteOptions { IsOrdered = false }, cancellationToken);
            }
        }
        var range = Builders<BsonDocument>.Filter.Gte("_id", 1) & Builders<BsonDocument>.Filter.Lte("_id", settings.Rows);
        using var cursor = await collection.FindAsync(range,
            new FindOptions<BsonDocument> { BatchSize = 1000 }, cancellationToken);
        long count = 0;
        while (await cursor.MoveNextAsync(cancellationToken))
        foreach (var document in cursor.Current)
        {
            if (!document.TryGetValue("payload", out var payload) || !payload.IsString || payload.AsString.Length != settings.PayloadBytes)
                throw new InvalidOperationException("MongoDB payload mismatch. Run seed with matching rows/payload-bytes.");
            count++;
        }
        if (count != settings.Rows)
            throw new InvalidOperationException("MongoDB dataset mismatch. Run seed with matching rows/payload-bytes.");
    }

    public async Task ReadAsync(int id, CancellationToken cancellationToken)
    {
        var document = await collection.Find(Builders<BsonDocument>.Filter.Eq("_id", id))
            .Project(projection).FirstOrDefaultAsync(cancellationToken);
        if (document is null || !document.TryGetValue("payload", out var payload) ||
            !payload.IsString || payload.AsString.Length != options.PayloadBytes)
            throw new InvalidOperationException("Missing row or unexpected payload.");
    }

    public ValueTask DisposeAsync()
    {
        client.Dispose();
        return ValueTask.CompletedTask;
    }
}
