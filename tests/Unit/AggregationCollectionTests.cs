using System.Reflection;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Snail.Toolkit.Mongodb.Tests.Unit;

/// <summary>
/// The aggregation helpers keep the collection they read. The database here is the driver's, wrapped so that
/// every collection it builds is counted and answers from memory: the helpers see the driver's behaviour and
/// nothing needs a server.
/// </summary>
public class AggregationCollectionTests
{
    private static readonly PipelineDefinition<BsonDocument, BsonDocument> Everything =
        new BsonDocument[] { new("$match", new BsonDocument()) };

    [Fact]
    public async Task AggregateToListAsync_OnTheSameCollection_BuildsItOnce()
    {
        var shop = new Shop();

        await shop.Database.AggregateToListAsync(Everything, "orders");
        await shop.Database.AggregateSingleAsync(Everything, "orders");

        Assert.Equal(["orders"], shop.Built);
        Assert.Equal(["shop.orders", "shop.orders"], shop.Read);
    }

    [Fact]
    public async Task AggregateToListAsync_OnAnotherCollection_ReadsThatOne()
    {
        var shop = new Shop();

        await shop.Database.AggregateToListAsync(Everything, "orders");
        await shop.Database.AggregateToListAsync(Everything, "audit");
        await shop.Database.AggregateToListAsync(Everything, "orders");

        Assert.Equal(["orders", "audit"], shop.Built);
        Assert.Equal(["shop.orders", "shop.audit", "shop.orders"], shop.Read);
    }

    [Fact]
    public async Task AggregateToListAsync_OnAnotherDatabase_ReadsThatOne()
    {
        var shop = new Shop("shop");
        var audit = new Shop("audit");

        await shop.Database.AggregateToListAsync(Everything, "orders");
        await audit.Database.AggregateToListAsync(Everything, "orders");

        Assert.Equal(["shop.orders"], shop.Read);
        Assert.Equal(["audit.orders"], audit.Read);
    }

    [Fact]
    public async Task AggregateToListAsync_PastTheHeldNames_StillReadsTheNamedCollection()
    {
        var shop = new Shop();

        for (var day = 0; day < 300; day++)
            await shop.Database.AggregateToListAsync(Everything, $"log_{day}");

        await shop.Database.AggregateToListAsync(Everything, "log_299");
        await shop.Database.AggregateToListAsync(Everything, "log_0");

        Assert.Equal("shop.log_299", shop.Read[^2]);
        Assert.Equal("shop.log_0", shop.Read[^1]);
        Assert.Equal(301, shop.Built.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task AggregateToListAsync_WithoutACollectionName_ThrowsAsTheDriverDoes(string? name)
    {
        var database = new MongoClient(Given.Connection).GetDatabase("shop");

        var expected = Record.Exception(() => database.GetCollection<BsonDocument>(name!));
        var actual = await Record.ExceptionAsync(() => database.AggregateToListAsync(Everything, name!));

        Assert.NotNull(expected);
        Assert.IsType(expected.GetType(), actual);
    }

    /// <summary>A database whose collections the driver builds and memory answers, recording both.</summary>
    private sealed class Shop
    {
        private readonly IMongoDatabase _driver;

        public Shop(string name = "shop")
        {
            _driver = new MongoClient(Given.Connection).GetDatabase(name);
            Database = Stub.Of<IMongoDatabase>(Collection);
        }

        public IMongoDatabase Database { get; }

        public List<string> Built { get; } = [];

        public List<string> Read { get; } = [];

        private object? Collection(MethodInfo method, object?[] args)
        {
            Assert.Equal(nameof(IMongoDatabase.GetCollection), method.Name);

            var collection = (IMongoCollection<BsonDocument>)method.Invoke(_driver, args)!;
            Built.Add(collection.CollectionNamespace.CollectionName);

            return Stub.Of<IMongoCollection<BsonDocument>>((called, _) =>
            {
                Assert.Equal(nameof(IMongoCollection<>.AggregateAsync), called.Name);
                Read.Add(collection.CollectionNamespace.FullName);

                return Task.FromResult<IAsyncCursor<BsonDocument>>(new Empty());
            });
        }
    }

    /// <summary>A cursor that yields nothing.</summary>
    private sealed class Empty : IAsyncCursor<BsonDocument>
    {
        public IEnumerable<BsonDocument> Current => [];

        public bool MoveNext(CancellationToken cancellationToken = default) => false;

        public Task<bool> MoveNextAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);

        public void Dispose()
        {
        }
    }
}
