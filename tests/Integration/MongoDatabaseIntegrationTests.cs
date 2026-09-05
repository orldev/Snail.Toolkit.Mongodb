using MongoDB.Bson;
using MongoDB.Driver;

namespace Snail.Toolkit.Mongodb.Tests.Integration;

[Collection(MongoContainerCollection.Name)]
public class MongoDatabaseIntegrationTests(MongoContainerFixture fixture)
{
    private static PipelineDefinition<BsonDocument, BsonDocument> Match(string product) =>
        new[] { new BsonDocument("$match", new BsonDocument("Product", product)) };

    [Fact]
    public async Task GetCollection_WithoutName_WritesToACollectionNamedAfterTheType()
    {
        var db = fixture.CreateDatabase();

        await db.GetCollection<Order>().InsertOneAsync(new Order { Product = "book", Quantity = 2 });

        var names = await (await db.ListCollectionNamesAsync()).ToListAsync();
        Assert.Contains(nameof(Order), names);
    }

    [Fact]
    public async Task GetCollection_RoundTripsADocument()
    {
        var db = fixture.CreateDatabase();
        var collection = db.GetCollection<Order>();

        await collection.InsertOneAsync(new Order { Product = "book", Quantity = 2 });

        var found = await collection
            .Find(Builders<Order>.Filter.Eq(x => x.Product, "book"))
            .FirstOrDefaultAsync();

        Assert.NotNull(found);
        Assert.Equal(2, found.Quantity);
    }

    [Fact]
    public async Task Update_AppliesToAStoredDocument()
    {
        var db = fixture.CreateDatabase();
        var collection = db.GetCollection<Order>();
        await collection.InsertOneAsync(new Order { Product = "book", Quantity = 2 });

        await collection.UpdateOneAsync(
            Builders<Order>.Filter.Eq(x => x.Product, "book"),
            Builders<Order>.Update.Set(x => x.Quantity, 5));

        var found = await collection.Find(Builders<Order>.Filter.Empty).SingleAsync();
        Assert.Equal(5, found.Quantity);
    }

    [Fact]
    public async Task Sort_OrdersResults()
    {
        var db = fixture.CreateDatabase();
        var collection = db.GetCollection<Order>();
        await collection.InsertManyAsync(
        [
            new Order { Product = "a", Quantity = 3 },
            new Order { Product = "b", Quantity = 1 },
            new Order { Product = "c", Quantity = 2 }
        ]);

        var found = await collection
            .Find(Builders<Order>.Filter.Empty)
            .Sort(Builders<Order>.Sort.Ascending(x => x.Quantity))
            .ToListAsync();

        Assert.Equal(["b", "c", "a"], found.Select(x => x.Product));
    }

    [Fact]
    public async Task AggregateAsync_ReturnsMatchingDocuments()
    {
        var db = fixture.CreateDatabase();
        await db.GetCollection<BsonDocument>("orders").InsertManyAsync(
        [
            new BsonDocument { { "Product", "book" }, { "Quantity", 2 } },
            new BsonDocument { { "Product", "pen" }, { "Quantity", 7 } }
        ]);

        using var cursor = await db.AggregateAsync(Match("book"), "orders");
        var documents = await cursor.ToListAsync();

        Assert.Single(documents);
        Assert.Equal(2, documents[0]["Quantity"].AsInt32);
    }

    [Fact]
    public async Task AggregateToListAsync_BuffersEveryDocument()
    {
        var db = fixture.CreateDatabase();
        await db.GetCollection<BsonDocument>("orders").InsertManyAsync(
        [
            new BsonDocument { { "Product", "book" }, { "Quantity", 2 } },
            new BsonDocument { { "Product", "book" }, { "Quantity", 5 } },
            new BsonDocument { { "Product", "pen" }, { "Quantity", 7 } }
        ]);

        var documents = await db.AggregateToListAsync(Match("book"), "orders");

        Assert.Equal(2, documents.Count);
        Assert.All(documents, d => Assert.Equal("book", d["Product"].AsString));
    }

    [Fact]
    public async Task AggregateToListAsync_WhenNothingMatches_ReturnsEmpty()
    {
        var db = fixture.CreateDatabase();
        await db.GetCollection<BsonDocument>("orders").InsertOneAsync(new BsonDocument("Product", "pen"));

        Assert.Empty(await db.AggregateToListAsync(Match("book"), "orders"));
    }

    [Fact]
    public async Task AggregateAsAsyncEnumerable_StreamsEveryDocument()
    {
        var db = fixture.CreateDatabase();
        await db.GetCollection<BsonDocument>("orders").InsertManyAsync(
        [
            new BsonDocument { { "Product", "book" }, { "Quantity", 2 } },
            new BsonDocument { { "Product", "book" }, { "Quantity", 5 } }
        ]);

        var quantities = new List<int>();

        await foreach (var document in db.AggregateAsAsyncEnumerable(Match("book"), "orders"))
            quantities.Add(document["Quantity"].AsInt32);

        Assert.Equal([2, 5], quantities);
    }

    /// <summary>
    /// Breaking out early must still dispose the cursor, which is the whole reason this method
    /// exists alongside the raw <c>AggregateAsync</c>.
    /// </summary>
    [Fact]
    public async Task AggregateAsAsyncEnumerable_CanBeAbandonedEarly()
    {
        var db = fixture.CreateDatabase();
        await db.GetCollection<BsonDocument>("orders").InsertManyAsync(
            Enumerable.Range(0, 50).Select(i => new BsonDocument { { "Product", "book" }, { "Quantity", i } }));

        var seen = 0;

        await foreach (var _ in db.AggregateAsAsyncEnumerable(Match("book"), "orders"))
        {
            seen++;
            if (seen == 3) break;
        }

        Assert.Equal(3, seen);
    }

    [Fact]
    public async Task AggregateSingleAsync_Generic_ReturnsATypedDocument()
    {
        var db = fixture.CreateDatabase();
        await db.GetCollection<Order>().InsertOneAsync(new Order { Product = "book", Quantity = 2 });

        PipelineDefinition<Order, Order> pipeline =
            new[] { new BsonDocument("$match", new BsonDocument("Product", "book")) };

        var order = await db.AggregateSingleAsync(pipeline, nameof(Order));

        Assert.NotNull(order);
        Assert.Equal(2, order.Quantity);
    }

    [Fact]
    public async Task AggregateSingleAsync_Generic_WhenNothingMatches_ReturnsNull()
    {
        var db = fixture.CreateDatabase();

        PipelineDefinition<Order, Order> pipeline =
            new[] { new BsonDocument("$match", new BsonDocument("Product", "book")) };

        Assert.Null(await db.AggregateSingleAsync(pipeline, nameof(Order)));
    }

    [Fact]
    public async Task AggregateSingleAsync_ReturnsTheOnlyDocument()
    {
        var db = fixture.CreateDatabase();
        await db.GetCollection<BsonDocument>("orders")
            .InsertOneAsync(new BsonDocument { { "Product", "book" }, { "Quantity", 2 } });

        var document = await db.AggregateSingleAsync(Match("book"), "orders");

        Assert.NotNull(document);
        Assert.Equal(2, document["Quantity"].AsInt32);
    }

    [Fact]
    public async Task AggregateSingleAsync_WhenNothingMatches_ReturnsNull()
    {
        var db = fixture.CreateDatabase();
        await db.GetCollection<BsonDocument>("orders")
            .InsertOneAsync(new BsonDocument("Product", "pen"));

        Assert.Null(await db.AggregateSingleAsync(Match("book"), "orders"));
    }

    /// <summary>
    /// The documented contract: more than one match is an error, not a silent first-wins.
    /// </summary>
    [Fact]
    public async Task AggregateSingleAsync_WhenMoreThanOneMatches_Throws()
    {
        var db = fixture.CreateDatabase();
        await db.GetCollection<BsonDocument>("orders").InsertManyAsync(
        [
            new BsonDocument("Product", "book"),
            new BsonDocument("Product", "book")
        ]);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => db.AggregateSingleAsync(Match("book"), "orders"));
    }

    [Fact]
    public async Task AggregateAsync_HonoursCancellation()
    {
        var db = fixture.CreateDatabase();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => db.AggregateAsync(Match("book"), "orders", cancellationToken: cts.Token));
    }

    [Fact]
    public async Task Database_IsUsableForOperationsOutsideTheWrapper()
    {
        var db = fixture.CreateDatabase();
        var collection = db.GetCollection<Order>();

        await collection.Indexes.CreateOneAsync(
            new CreateIndexModel<Order>(Builders<Order>.IndexKeys.Ascending(x => x.Product)));

        var indexes = await (await collection.Indexes.ListAsync()).ToListAsync();
        Assert.Contains(indexes, i => i["name"].AsString.StartsWith("Product", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Client_ReportsTheServerAsReachable()
    {
        var db = fixture.CreateDatabase();

        var result = await db.RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1));

        Assert.Equal(1.0, result["ok"].ToDouble());
    }

    /// <summary>
    /// Ownership is unambiguous now: whoever built the client owns it. Under DI that is the
    /// container, which is covered in <c>ServiceCollectionIntegrationTests</c>.
    /// </summary>
    [Fact]
    public async Task DisposingTheClient_StopsTheDatabaseWorking()
    {
        var db = fixture.CreateDatabase();
        await db.GetCollection<Order>().InsertOneAsync(new Order { Product = "book" });

        (db.Client as IDisposable)?.Dispose();

        await Assert.ThrowsAnyAsync<Exception>(
            () => db.GetCollection<Order>().Find(Builders<Order>.Filter.Empty).FirstOrDefaultAsync());
    }
}
