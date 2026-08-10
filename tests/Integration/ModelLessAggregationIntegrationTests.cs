using MongoDB.Bson;
using MongoDB.Driver;

namespace Snail.Toolkit.Mongodb.Tests.Integration;

/// <summary>
/// Aggregation in the shape most joins take: a <c>$lookup</c> and a <c>$project</c> whose output has
/// no model of its own, so the result stays a <see cref="BsonDocument"/> and no class has to be
/// carried around for it. Written with an inline array literal and no type arguments, which is what
/// the non-generic overloads exist for — type inference cannot reach
/// <see cref="PipelineDefinition{TInput,TOutput}"/> through its implicit conversion.
/// </summary>
[Collection(MongoContainerCollection.Name)]
public class ModelLessAggregationIntegrationTests(MongoContainerFixture fixture)
{
    private const string History = "HistoryCommonVehicle";
    private const string Users = "User";

    private static async Task<ObjectId> Seed(IMongoDatabase db, string deviceToken = "token-1")
    {
        var userId = ObjectId.GenerateNewId();
        var historyId = ObjectId.GenerateNewId();

        await db.GetCollection<BsonDocument>(Users).InsertOneAsync(new BsonDocument
        {
            { "_id", userId },
            { "DeviceToken", deviceToken }
        });

        await db.GetCollection<BsonDocument>(History).InsertOneAsync(new BsonDocument
        {
            { "_id", historyId },
            { "ToWhom", userId },
            { "IdentifierVehicle", "A-123" }
        });

        return historyId;
    }

    private static BsonDocument[] Pipeline(ObjectId id) =>
    [
        new("$match", new BsonDocument
        {
            { "_id", id },
            { "ToWhom", new BsonDocument("$exists", true) }
        }),
        new("$lookup", new BsonDocument
        {
            { "from", Users },
            { "localField", "ToWhom" },
            { "foreignField", "_id" },
            { "as", "User" }
        }),
        new("$unwind", "$User"),
        new("$project", new BsonDocument
        {
            { "DeviceToken", "$User.DeviceToken" },
            { "IdentifierVehicle", "$IdentifierVehicle" }
        })
    ];

    [Fact]
    public async Task AggregateSingleAsync_JoinsAndProjectsWithoutAModel()
    {
        var db = fixture.CreateDatabase();
        var id = await Seed(db);

        var document = await db.AggregateSingleAsync(Pipeline(id), History);

        Assert.NotNull(document);
        Assert.Equal("token-1", document["DeviceToken"].AsString);
        Assert.Equal("A-123", document["IdentifierVehicle"].AsString);
    }

    [Fact]
    public async Task AggregateSingleAsync_WhenTheJoinMatchesNothing_ReturnsNull()
    {
        var db = fixture.CreateDatabase();
        await Seed(db);

        var document = await db.AggregateSingleAsync(Pipeline(ObjectId.GenerateNewId()), History);

        Assert.Null(document);
    }

    /// <summary>
    /// A document whose <c>ToWhom</c> is absent is filtered out by the <c>$match</c>, so the
    /// <c>$unwind</c> never sees an empty join.
    /// </summary>
    [Fact]
    public async Task AggregateSingleAsync_WhenTheJoinFieldIsMissing_ReturnsNull()
    {
        var db = fixture.CreateDatabase();
        var id = ObjectId.GenerateNewId();

        await db.GetCollection<BsonDocument>(History).InsertOneAsync(new BsonDocument
        {
            { "_id", id },
            { "IdentifierVehicle", "A-123" }
        });

        Assert.Null(await db.AggregateSingleAsync(Pipeline(id), History));
    }

    [Fact]
    public async Task AggregateToListAsync_AcceptsTheSameInlinePipeline()
    {
        var db = fixture.CreateDatabase();
        var id = await Seed(db);

        var documents = await db.AggregateToListAsync(Pipeline(id), History);

        Assert.Single(documents);
        Assert.Equal("token-1", documents[0]["DeviceToken"].AsString);
    }

    [Fact]
    public async Task AggregateAsAsyncEnumerable_AcceptsTheSameInlinePipeline()
    {
        var db = fixture.CreateDatabase();
        var id = await Seed(db);

        var tokens = new List<string>();

        await foreach (var document in db.AggregateAsAsyncEnumerable(Pipeline(id), History))
            tokens.Add(document["DeviceToken"].AsString);

        Assert.Equal(["token-1"], tokens);
    }

    [Fact]
    public async Task AggregateAsync_AcceptsTheSameInlinePipeline()
    {
        var db = fixture.CreateDatabase();
        var id = await Seed(db);

        using var cursor = await db.AggregateAsync(Pipeline(id), History);
        var documents = await cursor.ToListAsync();

        Assert.Single(documents);
    }

    /// <summary>
    /// The middle ground: the source collection is typed, the projected result is not.
    /// </summary>
    [Fact]
    public async Task AggregateSingleAsync_ReadsATypedCollectionAndYieldsABsonDocument()
    {
        var db = fixture.CreateDatabase();
        await db.GetCollection<Order>().InsertOneAsync(new Order { Product = "book", Quantity = 2 });

        PipelineDefinition<Order, BsonDocument> pipeline = new BsonDocument[]
        {
            new("$match", new BsonDocument("Product", "book")),
            new("$project", new BsonDocument { { "_id", 0 }, { "Label", "$Product" } })
        };

        var document = await db.AggregateSingleAsync(pipeline, nameof(Order));

        Assert.NotNull(document);
        Assert.Equal("book", document["Label"].AsString);
        Assert.False(document.Contains("_id"));
    }
}
