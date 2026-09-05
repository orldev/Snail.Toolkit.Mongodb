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
    private IMongoClient? _client;

    public string Connection => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartAsync();

    public async Task DisposeAsync()
    {
        _client?.Dispose();

        await _container.DisposeAsync();
    }

    /// <summary>
    /// A database name unique to the calling test.
    /// </summary>
    public static string NewDatabaseName() => $"test_{Guid.NewGuid():N}";

    /// <summary>
    /// A database of its own, off the client this fixture owns.
    /// </summary>
    /// <remarks>
    /// Every call used to build its own client, so a suite of sixty tests left sixty connection
    /// pools and their monitoring threads behind — measured at twelve extra threads for sixty
    /// connected clients. Settings that shape the client still need one of their own.
    /// </remarks>
    public IMongoDatabase CreateDatabase(MongoSetup? setup = null) =>
        setup is null
            ? Client().GetDatabase(NewDatabaseName())
            : Mongo.Database(new MongoOptions { Connection = Connection, Database = NewDatabaseName() }, setup);

    private IMongoClient Client() =>
        _client ??= Mongo.Client(new MongoOptions { Connection = Connection, Database = NewDatabaseName() });

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
