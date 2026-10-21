using MongoDB.Driver;
using Testcontainers.MongoDb;

namespace Snail.Toolkit.Mongodb.Tests.Integration;

/// <summary>
/// A single-node replica set. Transactions and change streams need one — a standalone server
/// rejects them — so this is kept separate from <see cref="MongoContainerFixture"/>, whose
/// standalone container starts faster and covers everything else.
/// </summary>
public sealed class MongoReplicaSetFixture : IAsyncLifetime
{
    private readonly MongoDbContainer _container = new MongoDbBuilder("mongo:8.0")
        .WithReplicaSet("rs0")
        .Build();

    private readonly List<IMongoClient> _owned = [];

    private IMongoClient? _client;

    public string Connection => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartAsync();

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        _owned.ForEach(client => client.Dispose());

        await _container.DisposeAsync();
    }

    public IMongoDatabase CreateDatabase(MongoSetup? setup = null) =>
        setup is null
            ? Client().GetDatabase(MongoContainerFixture.NewDatabaseName())
            : Owned(setup).GetDatabase(MongoContainerFixture.NewDatabaseName());

    private IMongoClient Client() =>
        _client ??= Mongo.Client(Options());

    private IMongoClient Owned(MongoSetup setup)
    {
        var client = Mongo.Client(Options(), setup);
        _owned.Add(client);

        return client;
    }

    private MongoOptions Options() =>
        new() { Connection = Connection, Database = MongoContainerFixture.NewDatabaseName() };
}

[CollectionDefinition(Name)]
public sealed class MongoReplicaSetCollection : ICollectionFixture<MongoReplicaSetFixture>
{
    public const string Name = "mongo-replica-set";
}
