using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.GridFS;
using Testcontainers.MongoDb;

namespace Snail.Toolkit.Mongodb.Tests.Integration;

/// <summary>
/// Starts a single standalone MongoDB container for the whole collection. Tests share the server
/// but never a database: each one asks for its own, so they stay independent.
/// </summary>
public sealed class MongoContainerFixture : IAsyncLifetime
{
    private readonly MongoDbContainer _container = new MongoDbBuilder("mongo:8.0").Build();

    public string Connection => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    /// <summary>
    /// A database name unique to the calling test.
    /// </summary>
    public static string NewDatabaseName() => $"test_{Guid.NewGuid():N}";

    /// <summary>
    /// A database of its own, on a client the test owns.
    /// </summary>
    public IMongoDatabase CreateDatabase(MongoSetup? setup = null) =>
        Mongo.Database(
            new MongoOptions { Connection = Connection, Database = NewDatabaseName() }, setup);

    public IGridFSBucket CreateGridFs(MongoSetup? setup = null) =>
        Mongo.GridFs(CreateDatabase(setup), setup);
}

[CollectionDefinition(Name)]
public sealed class MongoContainerCollection : ICollectionFixture<MongoContainerFixture>
{
    public const string Name = "mongo";
}

/// <summary>
/// A document type used to exercise the default collection naming convention.
/// </summary>
public sealed class Order
{
    public ObjectId Id { get; set; }

    public string Product { get; set; } = string.Empty;

    public int Quantity { get; set; }
}
