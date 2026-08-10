using MongoDB.Driver;

namespace Snail.Toolkit.Mongodb.Tests.Integration;

[Collection(MongoReplicaSetCollection.Name)]
public class TransactionIntegrationTests(MongoReplicaSetFixture fixture)
{
    /// <summary>
    /// The helper hangs off <see cref="IMongoClient"/>, so it works without this library's service
    /// in the picture at all.
    /// </summary>
    [Fact]
    public async Task InTransactionAsync_WorksDirectlyOnTheDriverClient()
    {
        using var client = new MongoClient(fixture.Connection);
        var collection = client
            .GetDatabase(MongoContainerFixture.NewDatabaseName())
            .GetCollection<Order>();

        await client.InTransactionAsync(async (session, ct) =>
        {
            await collection.InsertOneAsync(session, new Order { Product = "book" }, cancellationToken: ct);
        });

        Assert.Equal(1, await collection.CountDocumentsAsync(Builders<Order>.Filter.Empty));
    }

    [Fact]
    public async Task InTransactionAsync_OnTheDriverClient_RollsBackOnFailure()
    {
        using var client = new MongoClient(fixture.Connection);
        var collection = client
            .GetDatabase(MongoContainerFixture.NewDatabaseName())
            .GetCollection<Order>();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.InTransactionAsync(async (session, ct) =>
            {
                await collection.InsertOneAsync(session, new Order { Product = "book" }, cancellationToken: ct);
                throw new InvalidOperationException("no");
            }));

        Assert.Equal(0, await collection.CountDocumentsAsync(Builders<Order>.Filter.Empty));
    }

    [Fact]
    public async Task InTransactionAsync_CommitsWhenTheBodyReturns()
    {
        var db = fixture.CreateDatabase();
        var collection = db.GetCollection<Order>();

        await db.Client.InTransactionAsync(async (session, ct) =>
        {
            await collection.InsertOneAsync(session, new Order { Product = "book" }, cancellationToken: ct);
            await collection.InsertOneAsync(session, new Order { Product = "pen" }, cancellationToken: ct);
        });

        Assert.Equal(2, await collection.CountDocumentsAsync(Builders<Order>.Filter.Empty));
    }

    [Fact]
    public async Task InTransactionAsync_ReturnsTheBodyResult()
    {
        var db = fixture.CreateDatabase();
        var collection = db.GetCollection<Order>();

        var count = await db.Client.InTransactionAsync(async (session, ct) =>
        {
            await collection.InsertOneAsync(session, new Order { Product = "book" }, cancellationToken: ct);
            return await collection.CountDocumentsAsync(session, Builders<Order>.Filter.Empty, cancellationToken: ct);
        });

        Assert.Equal(1, count);
    }

    /// <summary>
    /// The point of a transaction: a failure part-way through leaves nothing behind.
    /// </summary>
    [Fact]
    public async Task InTransactionAsync_WhenTheBodyThrows_RollsBack()
    {
        var db = fixture.CreateDatabase();
        var collection = db.GetCollection<Order>();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            db.Client.InTransactionAsync(async (session, ct) =>
            {
                await collection.InsertOneAsync(session, new Order { Product = "book" }, cancellationToken: ct);
                throw new InvalidOperationException("no");
            }));

        Assert.Equal(0, await collection.CountDocumentsAsync(Builders<Order>.Filter.Empty));
    }

    [Fact]
    public async Task InTransactionAsync_WritesAreInvisibleUntilCommit()
    {
        var db = fixture.CreateDatabase();
        var collection = db.GetCollection<Order>();
        var seenMidTransaction = -1L;

        await db.Client.InTransactionAsync(async (session, ct) =>
        {
            await collection.InsertOneAsync(session, new Order { Product = "book" }, cancellationToken: ct);

            // Reads outside the session must not see the uncommitted write.
            seenMidTransaction = await collection.CountDocumentsAsync(
                Builders<Order>.Filter.Empty, cancellationToken: ct);
        });

        Assert.Equal(0, seenMidTransaction);
        Assert.Equal(1, await collection.CountDocumentsAsync(Builders<Order>.Filter.Empty));
    }

    [Fact]
    public async Task InTransactionAsync_HonoursTheGivenTransactionOptions()
    {
        var db = fixture.CreateDatabase();
        var collection = db.GetCollection<Order>();

        var options = new TransactionOptions(
            readConcern: ReadConcern.Snapshot,
            writeConcern: WriteConcern.WMajority);

        await db.Client.InTransactionAsync(
            async (session, ct) =>
            {
                await collection.InsertOneAsync(session, new Order { Product = "book" }, cancellationToken: ct);
            },
            options);

        Assert.Equal(1, await collection.CountDocumentsAsync(Builders<Order>.Filter.Empty));
    }
}
