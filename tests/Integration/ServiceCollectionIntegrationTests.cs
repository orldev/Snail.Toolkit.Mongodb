using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.GridFS;
using Snail.Toolkit.Mongodb.DependencyInjection;

namespace Snail.Toolkit.Mongodb.Tests.Integration;

[Collection(MongoContainerCollection.Name)]
public class ServiceCollectionIntegrationTests(MongoContainerFixture fixture)
{
    private ServiceProvider Provider(string database)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "mongodb:Connection", fixture.Connection },
                { "mongodb:Database", database }
            })
            .Build();

        return new ServiceCollection().AddMongodb(configuration).BuildServiceProvider();
    }

    /// <summary>
    /// A repository that only reads takes <see cref="IMongoDatabase"/> and never sees a client —
    /// which is the whole reason there is no carrier type any more.
    /// </summary>
    [Fact]
    public async Task ResolvedDatabase_TalksToTheServer()
    {
        var name = MongoContainerFixture.NewDatabaseName();
        await using var provider = Provider(name);

        var db = provider.GetRequiredService<IMongoDatabase>();
        await db.GetCollection<Order>().InsertOneAsync(new Order { Product = "book", Quantity = 2 });

        var found = await db.GetCollection<Order>().Find(Builders<Order>.Filter.Empty).SingleAsync();

        Assert.Equal("book", found.Product);
        Assert.Equal(name, db.DatabaseNamespace.DatabaseName);
    }

    [Fact]
    public async Task ResolvedDatabase_ComesOffTheResolvedClient()
    {
        await using var provider = Provider(MongoContainerFixture.NewDatabaseName());

        Assert.Same(
            provider.GetRequiredService<IMongoClient>(),
            provider.GetRequiredService<IMongoDatabase>().Client);
    }

    [Fact]
    public async Task ResolvedBucket_RoundTripsAFile()
    {
        await using var provider = Provider(MongoContainerFixture.NewDatabaseName());
        var bucket = provider.GetRequiredService<IGridFSBucket>();

        var id = await bucket.UploadFromBytesAsync(
            "file.txt",
            "content"u8.ToArray(),
            new GridFSUploadOptions { Metadata = new BsonDocument("ContentType", "text/plain") });

        var found = await bucket.FindOneByObjectIdAsync(id);

        Assert.Equal("text/plain", found?.ContentType);
    }

    [Fact]
    public async Task ResolvedBucket_SitsOnTheResolvedDatabase()
    {
        await using var provider = Provider(MongoContainerFixture.NewDatabaseName());

        Assert.Same(
            provider.GetRequiredService<IMongoDatabase>(),
            provider.GetRequiredService<IGridFSBucket>().Database);
    }

    /// <summary>
    /// Two databases side by side, each with its own client — the case the unkeyed registration
    /// cannot express.
    /// </summary>
    [Fact]
    public async Task AddKeyedMongo_KeepsTwoDatabasesApart()
    {
        await using var provider = new ServiceCollection()
            .AddKeyedMongodb("orders", o =>
            {
                o.Connection = fixture.Connection;
                o.Database = MongoContainerFixture.NewDatabaseName();
            })
            .AddKeyedMongodb("audit", o =>
            {
                o.Connection = fixture.Connection;
                o.Database = MongoContainerFixture.NewDatabaseName();
            })
            .BuildServiceProvider();

        var orders = provider.GetRequiredKeyedService<IMongoDatabase>("orders");
        var audit = provider.GetRequiredKeyedService<IMongoDatabase>("audit");

        await orders.GetCollection<Order>().InsertOneAsync(new Order { Product = "book" });

        Assert.Equal(1, await orders.GetCollection<Order>().CountDocumentsAsync(Builders<Order>.Filter.Empty));
        Assert.Equal(0, await audit.GetCollection<Order>().CountDocumentsAsync(Builders<Order>.Filter.Empty));
        Assert.NotSame(orders.Client, audit.Client);
    }

    /// <summary>
    /// The container owns the client it created, so disposing the provider must release it and its
    /// connection pool.
    /// </summary>
    [Fact]
    public async Task DisposingTheProvider_DisposesTheClient()
    {
        var provider = Provider(MongoContainerFixture.NewDatabaseName());
        var db = provider.GetRequiredService<IMongoDatabase>();

        await db.RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1));
        await provider.DisposeAsync();

        await Assert.ThrowsAnyAsync<Exception>(
            () => db.RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1)));
    }
}
