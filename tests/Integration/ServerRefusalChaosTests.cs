using MongoDB.Bson;
using MongoDB.Driver;

namespace Snail.Toolkit.Mongodb.Tests.Integration;

/// <summary>
/// The rule for a database name mirrors the server's. Both halves are checked against mongo:8.0 through the driver
/// alone, so the validator is not asked about its own rule: the names it refuses the server refuses, and names at
/// the edge of the rule the server accepts.
/// </summary>
[Collection(MongoContainerCollection.Name)]
public class ServerRefusalChaosTests(MongoContainerFixture fixture)
{
    private const string SixtyFourCharacters = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private const string SixtyThreeCharacters = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Theory]
    [InlineData("orders/2026")]
    [InlineData("a$b")]
    [InlineData("back\\slash")]
    [InlineData("with\"quote")]
    [InlineData("with space")]
    [InlineData(SixtyFourCharacters)]
    public async Task InsertOneAsync_IntoADatabaseTheRuleRefuses_IsRefusedByTheServerToo(string database)
    {
        using var client = new MongoClient(fixture.Connection);
        var collection = client.GetDatabase(database).GetCollection<BsonDocument>("orders");

        await Assert.ThrowsAsync<MongoCommandException>(() => collection.InsertOneAsync(new BsonDocument("x", 1)));
    }

    [Theory]
    [InlineData(SixtyThreeCharacters)]
    [InlineData("orders-2026")]
    [InlineData("orders_v2")]
    [InlineData("Заказы")]
    public async Task InsertOneAsync_IntoADatabaseAtTheEdgeOfTheRule_Succeeds(string database)
    {
        var db = fixture.CreateDatabase(database);

        await db.GetCollection<BsonDocument>("orders").InsertOneAsync(new BsonDocument("x", 1));

        Assert.Equal(1, await db.GetCollection<BsonDocument>("orders").CountDocumentsAsync(new BsonDocument()));
    }
}
