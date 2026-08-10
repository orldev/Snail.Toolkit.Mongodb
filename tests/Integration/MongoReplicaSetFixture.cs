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

    public string Connection => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public IMongoDatabase CreateDatabase(MongoSetup? setup = null) =>
        MongoFactory.CreateDatabase(
            new MongoOptions
            {
                Connection = Connection,
                Database = MongoContainerFixture.NewDatabaseName()
            },
            setup);
}

[CollectionDefinition(Name)]
public sealed class MongoReplicaSetCollection : ICollectionFixture<MongoReplicaSetFixture>
{
    public const string Name = "mongo-replica-set";
}
