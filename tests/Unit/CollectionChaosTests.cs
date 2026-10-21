using MongoDB.Bson;
using MongoDB.Driver;

namespace Snail.Toolkit.Mongodb.Tests.Unit;

/// <summary>
/// The collection helper under the first burst of a cold start and under names nobody meant.
/// </summary>
public class CollectionChaosTests
{
    /// <summary>
    /// The remark promises one collection per database and type. Sixty-four requests asking at once, before any was
    /// held, each built their own and the last writer won — nine different objects went out for one type. The first
    /// one added is now the one every caller gets.
    /// </summary>
    [Fact]
    public void GetCollection_FromManyThreadsAtOnce_HandsBackOneCollection()
    {
        var database = new MongoClient(Given.Connection).GetDatabase($"burst_{Guid.NewGuid():N}");
        var collections = new object[64];

        Parallel.For(0, collections.Length, n => collections[n] = database.GetCollection<BsonDocument>());

        Assert.Single(collections.Distinct());
    }

    /// <summary>
    /// A convention that answers with whitespace is refused against the convention; whitespace given outright as
    /// the name goes through to the driver, which refuses it itself.
    /// </summary>
    [Theory]
    [InlineData(" ")]
    [InlineData("\t")]
    public void GetCollection_WithAWhitespaceName_ThrowsArgumentException(string name)
    {
        var database = new MongoClient(Given.Connection).GetDatabase("shop");

        Assert.ThrowsAny<ArgumentException>(() => database.GetCollection<BsonDocument>(name));
    }
}
