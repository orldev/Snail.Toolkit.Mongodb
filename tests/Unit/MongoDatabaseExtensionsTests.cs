using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Snail.Toolkit.Mongodb.Tests.Unit;

/// <summary>
/// The helpers hang off the driver's own types, so they work on a database or client obtained any way
/// at all — from DI, from <see cref="MongoFactory"/>, or from a hand-built <see cref="MongoClient"/>.
/// Building a client and asking it for a database opens no connections, so none of this needs a server.
/// </summary>
public class MongoDatabaseExtensionsTests
{
    private sealed record Page<T>;

    private static IMongoDatabase Database(string name = "orders") =>
        new MongoClient(Helper.Connection).GetDatabase(name);

    [Fact]
    public void GetCollection_WithoutName_UsesTheDocumentTypeName()
    {
        Assert.Equal(
            nameof(BsonDocument),
            Database().GetCollection<BsonDocument>().CollectionNamespace.CollectionName);
    }

    [Fact]
    public void GetCollection_WithName_UsesTheGivenName()
    {
        Assert.Equal(
            "files",
            Database().GetCollection<BsonDocument>("files").CollectionNamespace.CollectionName);
    }

    [Fact]
    public void GetCollection_UnwrapsGenericTypeNames()
    {
        Assert.Equal(
            "PageOfBsonDocument",
            Database().GetCollection<Page<BsonDocument>>().CollectionNamespace.CollectionName);
    }

    [Fact]
    public void GetCollection_HonoursAGivenNamingConvention()
    {
        Assert.Equal(
            "bsondocuments",
            Database()
                .GetCollection<BsonDocument>(naming: type => $"{type.Name.ToLowerInvariant()}s")
                .CollectionNamespace.CollectionName);
    }

    [Fact]
    public void GetCollection_WhenNameIsGiven_IgnoresTheConvention()
    {
        Assert.Equal(
            "explicit",
            Database()
                .GetCollection<BsonDocument>("explicit", _ => "convention")
                .CollectionNamespace.CollectionName);
    }

    [Fact]
    public void GetCollection_LivesInTheGivenDatabase()
    {
        Assert.Equal(
            "audit.files",
            Database("audit").GetCollection<BsonDocument>("files").CollectionNamespace.FullName);
    }

    [Fact]
    public void GetCollection_FollowsTheProcessWideDefaultConvention()
    {
        var previous = MongoNaming.Default;

        try
        {
            MongoNaming.Default = type => $"{type.Name}Collection";

            Assert.Equal(
                "BsonDocumentCollection",
                Database().GetCollection<BsonDocument>().CollectionNamespace.CollectionName);
        }
        finally
        {
            MongoNaming.Default = previous;
        }
    }

    [Fact]
    public void GetCollection_WhenDatabaseIsNull_ThrowsArgumentNullException()
    {
        IMongoDatabase database = null!;

        Assert.Throws<ArgumentNullException>(() => database.GetCollection<BsonDocument>());
    }

    [Fact]
    public async Task Aggregate_WhenDatabaseIsNull_ThrowsArgumentNullException()
    {
        IMongoDatabase database = null!;
        PipelineDefinition<BsonDocument, BsonDocument> pipeline =
            new BsonDocument[] { new("$match", new BsonDocument()) };

        await Assert.ThrowsAsync<ArgumentNullException>(() => database.AggregateAsync(pipeline, "c"));
        await Assert.ThrowsAsync<ArgumentNullException>(() => database.AggregateSingleAsync(pipeline, "c"));
        await Assert.ThrowsAsync<ArgumentNullException>(() => database.AggregateToListAsync(pipeline, "c"));
    }

    [Fact]
    public async Task InTransactionAsync_WhenClientIsNull_ThrowsArgumentNullException()
    {
        IMongoClient client = null!;

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => client.InTransactionAsync((_, _) => Task.FromResult(1)));
    }

    [Fact]
    public async Task InTransactionAsync_WithNullBody_ThrowsArgumentNullException()
    {
        var client = new MongoClient(Helper.Connection);

        await Assert.ThrowsAsync<ArgumentNullException>(() => client.InTransactionAsync<int>(null!));

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => client.InTransactionAsync((Func<IClientSessionHandle, CancellationToken, Task>)null!));
    }

    [Fact]
    public void CreateDatabase_UsesTheConfiguredName()
    {
        var db = MongoFactory.CreateDatabase(
            new MongoOptions { Connection = Helper.Connection, Database = "orders" });

        Assert.Equal("orders", db.DatabaseNamespace.DatabaseName);
    }

    [Theory]
    [InlineData(null, Helper.Database, nameof(MongoOptions.Connection))]
    [InlineData("", Helper.Database, nameof(MongoOptions.Connection))]
    [InlineData("   ", Helper.Database, nameof(MongoOptions.Connection))]
    [InlineData(Helper.Connection, null, nameof(MongoOptions.Database))]
    [InlineData(Helper.Connection, "", nameof(MongoOptions.Database))]
    public void CreateClient_WithIncompleteOptions_ThrowsOptionsValidationException(
        string? connection, string? database, string expected)
    {
        var options = new MongoOptions { Connection = connection, Database = database };

        var exception = Assert.Throws<OptionsValidationException>(() => MongoFactory.CreateClient(options));

        Assert.Contains(expected, exception.Message);
    }

    [Fact]
    public void CreateClient_WithNullOptions_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => MongoFactory.CreateClient(null!));
    }

    [Fact]
    public void CreateClient_AppliesTheClientHook()
    {
        var applied = false;

        var client = MongoFactory.CreateClient(
            new MongoOptions { Connection = Helper.Connection, Database = Helper.Database },
            new MongoSetup
            {
                ConfigureClient = settings =>
                {
                    applied = true;
                    settings.ApplicationName = "toolkit-tests";
                }
            });

        Assert.True(applied);
        Assert.Equal("toolkit-tests", client.Settings.ApplicationName);
    }

    /// <summary>
    /// The driver's convention registry is process-global and has no upsert, so creating a client
    /// twice must not stack duplicate packs.
    /// </summary>
    [Fact]
    public void CreateClient_RegistersConventionsIdempotently()
    {
        var packName = $"test-{Guid.NewGuid():N}";

        MongoSetup Setup() => new()
        {
            ConventionPackName = packName,
            Conventions = pack => pack.Add(new MongoDB.Bson.Serialization.Conventions.CamelCaseElementNameConvention())
        };

        try
        {
            var options = new MongoOptions { Connection = Helper.Connection, Database = Helper.Database };
            MongoFactory.CreateClient(options, Setup());
            MongoFactory.CreateClient(options, Setup());

            var registered = MongoDB.Bson.Serialization.Conventions.ConventionRegistry
                .Lookup(typeof(BsonDocument))
                .Conventions
                .OfType<MongoDB.Bson.Serialization.Conventions.CamelCaseElementNameConvention>()
                .Count();

            Assert.Equal(1, registered);
        }
        finally
        {
            MongoDB.Bson.Serialization.Conventions.ConventionRegistry.Remove(packName);
        }
    }

    [Fact]
    public void CreateGridFsBucket_WhenDatabaseIsNull_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => MongoFactory.CreateGridFsBucket(null!));
    }
}
