using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Driver;

namespace Snail.Toolkit.Mongodb.Tests.Unit;

/// <summary>
/// The helpers hang off the driver's own types, so they work on a database or client obtained any way
/// at all — from DI, from <see cref="Mongo"/>, or from a hand-built <see cref="MongoClient"/>.
/// Building a client and asking it for a database opens no connections, so none of this needs a server.
/// </summary>
public class MongoDatabaseExtensionsTests
{
    private sealed record Page<T>;

    private static IMongoDatabase Database(string name = "orders") =>
        new MongoClient(Given.Connection).GetDatabase(name);

    [Fact]
    public void GetCollection_WithoutName_UsesTheDocumentTypeName()
    {
        Assert.Equal(
            nameof(BsonDocument),
            Database().GetCollection<BsonDocument>().CollectionNamespace.CollectionName);
    }

    /// <summary>A repository asks for its collection per request; the driver would build a new one every time.</summary>
    [Fact]
    public void GetCollection_WithoutName_HandsBackTheSameCollectionEachTime()
    {
        var database = Database();

        Assert.Same(database.GetCollection<BsonDocument>(), database.GetCollection<BsonDocument>());
    }

    [Fact]
    public void GetCollection_WithoutName_IsTheCollectionTheDriverBuilds()
    {
        var database = Database();
        var built = database.GetCollection<BsonDocument>(nameof(BsonDocument));

        var held = database.GetCollection<BsonDocument>();

        Assert.Equal(built.CollectionNamespace, held.CollectionNamespace);
        Assert.Equal(built.Settings, held.Settings);
        Assert.Same(built.DocumentSerializer, held.DocumentSerializer);
    }

    [Fact]
    public void GetCollection_WithoutName_KeepsEachDatabaseApart()
    {
        var orders = Database("orders").GetCollection<BsonDocument>();
        var audit = Database("audit").GetCollection<BsonDocument>();

        Assert.Equal("orders.BsonDocument", orders.CollectionNamespace.FullName);
        Assert.Equal("audit.BsonDocument", audit.CollectionNamespace.FullName);
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
    public void GetCollection_HonoursTheGivenCollectionSettings()
    {
        var collection = Database().GetCollection<BsonDocument>(
            settings: new MongoCollectionSettings { WriteConcern = WriteConcern.WMajority });

        Assert.Equal(WriteConcern.WMajority, collection.Settings.WriteConcern);
    }

    /// <summary>
    /// The naming convention is caller-supplied, so it can return anything. When it returned nothing
    /// the driver complained about a parameter called "name" that this caller never passed — the
    /// convention is the thing to fix, and the message has to say so.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void GetCollection_WhenTheConventionYieldsNothing_ThrowsNamingTheConvention(string produced)
    {
        var exception = Assert.Throws<ArgumentException>(
            () => Database().GetCollection<BsonDocument>(naming: _ => produced));

        Assert.Equal("naming", exception.ParamName);
    }

    [Fact]
    public void GetCollection_WhenTheConventionYieldsNull_ThrowsNamingTheConvention()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => Database().GetCollection<BsonDocument>(naming: _ => null!));

        Assert.Equal("naming", exception.ParamName);
    }

    /// <summary>
    /// The pack lands in the driver's process-global registry, which has no upsert: a second
    /// registration of the same name stacks a duplicate rather than replacing it.
    /// </summary>
    [Fact]
    public void ApplyConventions_UnderParallelCallers_RegistersThePackOnce()
    {
        var packName = $"test-{Guid.NewGuid():N}";
        var before = CamelCasingConventions();

        try
        {
            Parallel.For(0, 64, _ => Mongo.ApplyConventions(new MongoSetup
            {
                ConventionPackName = packName,
                Conventions = pack => pack.Add(new CamelCaseElementNameConvention())
            }));

            Assert.Equal(before + 1, CamelCasingConventions());
        }
        finally
        {
            ConventionRegistry.Remove(packName);
        }
    }

    /// <summary>
    /// A server that is not there must surface as a failure, never as an empty result: an
    /// aggregation that quietly returns nothing would read as "no matching documents".
    /// </summary>
    [Fact]
    public async Task AggregateToListAsync_WhenTheServerIsUnreachable_Throws()
    {
        var pipeline = new BsonDocument[] { new("$match", new BsonDocument()) };

        var exception = await Record.ExceptionAsync(
            () => Given.Database(Given.UnreachableConnection).AggregateToListAsync(pipeline, "orders"));

        Assert.NotNull(exception);
    }

    [Fact]
    public async Task AggregateToListAsync_WithACancelledToken_DoesNotReachTheServer()
    {
        var pipeline = new BsonDocument[] { new("$match", new BsonDocument()) };

        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Given.Database(Given.UnreachableConnection)
                .AggregateToListAsync(pipeline, "orders", cancellationToken: cancellation.Token));
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

    /// <summary>
    /// A caller that starts an aggregation and awaits it later sees the refusal in the task, not at the call.
    /// </summary>
    [Fact]
    public async Task Aggregate_WhenDatabaseIsNull_FaultsTheTaskRatherThanThrowing()
    {
        IMongoDatabase database = null!;
        PipelineDefinition<BsonDocument, BsonDocument> pipeline =
            new BsonDocument[] { new("$match", new BsonDocument()) };

        Task[] started =
        [
            database.AggregateAsync(pipeline, "c"),
            database.AggregateSingleAsync(pipeline, "c"),
            database.AggregateToListAsync(pipeline, "c")
        ];
        var streamed = database.AggregateAsAsyncEnumerable(pipeline, "c").GetAsyncEnumerator();

        Assert.All(started, task => Assert.IsType<ArgumentNullException>(task.Exception?.InnerException));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await streamed.MoveNextAsync());
    }

    [Fact]
    public async Task InTransactionAsync_WhenClientIsNull_ThrowsArgumentNullException()
    {
        IMongoClient client = null!;

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => client.InTransactionAsync((_, _) => Task.FromResult(1)));
    }

    /// <summary>
    /// Work that sends nothing starts and commits a transaction without a server, so the driver's real session,
    /// retry loop and commit run here.
    /// </summary>
    [Fact]
    public async Task InTransactionAsync_WithWorkThatSendsNothing_RunsItOnceInsideATransaction()
    {
        using var client = new MongoClient(Given.UnreachableConnection);
        var runs = new List<bool>();

        var result = await client.InTransactionAsync((session, _) =>
        {
            runs.Add(session.IsInTransaction);

            return Task.FromResult(42);
        });

        await client.InTransactionAsync((session, _) =>
        {
            runs.Add(session.IsInTransaction);

            return Task.CompletedTask;
        });

        Assert.Equal(42, result);
        Assert.Equal([true, true], runs);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InTransactionAsync_WhenTheBodyFailsTransiently_RunsItAgain(bool isThrownBeforeATask)
    {
        using var client = new MongoClient(Given.UnreachableConnection);
        var runs = 0;

        Func<IClientSessionHandle, CancellationToken, Task<int>> failingAtOnce = (_, _) =>
            ++runs == 1 ? throw Transient() : Task.FromResult(runs);

        Func<IClientSessionHandle, CancellationToken, Task<int>> failingLater = async (_, _) =>
        {
            await Task.Yield();

            return ++runs == 1 ? throw Transient() : runs;
        };

        var result = await client.InTransactionAsync(isThrownBeforeATask ? failingAtOnce : failingLater);

        Assert.Equal(2, result);
    }

    [Fact]
    public async Task InTransactionAsync_WithNullBody_ThrowsArgumentNullException()
    {
        var client = new MongoClient(Given.Connection);

        await Assert.ThrowsAsync<ArgumentNullException>(() => client.InTransactionAsync<int>(null!));

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => client.InTransactionAsync((Func<IClientSessionHandle, CancellationToken, Task>)null!));
    }

    [Fact]
    public void CreateDatabase_UsesTheConfiguredName()
    {
        var db = Mongo.Database(
            new MongoOptions { Connection = Given.Connection, Database = "orders" });

        Assert.Equal("orders", db.DatabaseNamespace.DatabaseName);
    }

    [Theory]
    [InlineData(null, Given.DatabaseName, nameof(MongoOptions.Connection))]
    [InlineData("", Given.DatabaseName, nameof(MongoOptions.Connection))]
    [InlineData("   ", Given.DatabaseName, nameof(MongoOptions.Connection))]
    [InlineData(Given.Connection, null, nameof(MongoOptions.Database))]
    [InlineData(Given.Connection, "", nameof(MongoOptions.Database))]
    public void CreateClient_WithIncompleteOptions_ThrowsOptionsValidationException(
        string? connection, string? database, string expected)
    {
        var options = new MongoOptions { Connection = connection, Database = database };

        var exception = Assert.Throws<OptionsValidationException>(() => Mongo.Client(options));

        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateClient_WithNullOptions_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => Mongo.Client(null!));
    }

    [Fact]
    public void CreateClient_AppliesTheClientHook()
    {
        var applied = false;

        var client = Mongo.Client(
            new MongoOptions { Connection = Given.Connection, Database = Given.DatabaseName },
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
    /// The driver's registry has no upsert, so creating a client twice must not stack duplicate
    /// packs. The count is taken as a delta because the registry is process-global: any other pack
    /// carrying the same convention is none of this test's business.
    /// </summary>
    [Fact]
    public void CreateClient_RegistersConventionsOncePerPackName()
    {
        var packName = $"test-{Guid.NewGuid():N}";

        MongoSetup Setup() => new()
        {
            ConventionPackName = packName,
            Conventions = pack => pack.Add(new CamelCaseElementNameConvention())
        };

        var before = CamelCasingConventions();

        try
        {
            var options = new MongoOptions { Connection = Given.Connection, Database = Given.DatabaseName };
            Mongo.Client(options, Setup());
            Mongo.Client(options, Setup());

            Assert.Equal(before + 1, CamelCasingConventions());
        }
        finally
        {
            ConventionRegistry.Remove(packName);
        }
    }

    private static int CamelCasingConventions() =>
        ConventionRegistry.Lookup(typeof(BsonDocument))
            .Conventions
            .OfType<CamelCaseElementNameConvention>()
            .Count();

    [Fact]
    public void CreateGridFsBucket_WhenDatabaseIsNull_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => Mongo.GridFs(null!));
    }

    private static MongoException Transient()
    {
        var exception = new MongoException("The primary stepped down.");
        exception.AddErrorLabel("TransientTransactionError");

        return exception;
    }
}
