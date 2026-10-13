using System.Reflection;
using BenchmarkDotNet.Attributes;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver;
using Snail.Toolkit.Mongodb.Extensions;

namespace Snail.Toolkit.Mongodb.Benchmarks;

/// <summary>The aggregation helpers as a repository calls them per request: a pipeline over a named collection.</summary>
/// <remarks>
/// The database stands in for the driver's: each collection it is asked for is built by a real database of a client
/// that has nothing to connect to, so that cost is paid as the driver pays it, and then answers the aggregation from
/// memory after rendering the pipeline to BSON as the driver renders it before sending. What remains is the work the
/// library adds around the driver.
/// </remarks>
[MemoryDiagnoser]
public class AggregateBenchmarks
{
    private const string Orders = "orders";

    private static readonly PipelineDefinition<BsonDocument, BsonDocument> Paid =
        new BsonDocument[] { new("$match", new BsonDocument("status", "paid")) };

    private MongoClient _client = null!;

    private Shop _many = null!;

    private Shop _one = null!;

    /// <summary>A database of ten paid orders and one of a single order, both over a client that never connects.</summary>
    /// <returns>A task that completes once every helper was checked against the driver.</returns>
    [GlobalSetup]
    public async Task Setup()
    {
        _client = new MongoClient("mongodb://127.0.0.1:1/?serverSelectionTimeoutMS=100");

        var database = _client.GetDatabase("shop");

        _many = new Shop(database, 10);
        _one = new Shop(database, 1);

        await VerifyAsync().ConfigureAwait(false);
    }

    /// <summary>Releases the client.</summary>
    [GlobalCleanup]
    public void Cleanup() => _client.Dispose();

    /// <summary>Ten documents, buffered.</summary>
    /// <returns>The documents.</returns>
    [Benchmark(Description = "AggregateToListAsync (10 documents)")]
    public Task<List<BsonDocument>> ToList() => _many.Database.AggregateToListAsync(Paid, Orders);

    /// <summary>The only document of a pipeline.</summary>
    /// <returns>The document.</returns>
    [Benchmark(Description = "AggregateSingleAsync")]
    public Task<BsonDocument?> Single() => _one.Database.AggregateSingleAsync(Paid, Orders);

    /// <summary>Ten documents, streamed.</summary>
    /// <returns>How many documents came.</returns>
    [Benchmark(Description = "AggregateAsAsyncEnumerable (10 documents)")]
    public async Task<int> Stream()
    {
        var count = 0;

        await foreach (var _ in _many.Database.AggregateAsAsyncEnumerable(Paid, Orders).ConfigureAwait(false))
            count++;

        return count;
    }

    /// <summary>Fails the run unless every helper reads the named collection with the pipeline the driver would send.</summary>
    /// <returns>A task that completes once every helper was checked.</returns>
    private async Task VerifyAsync()
    {
        var paid = Shop.Render(Paid);
        var limited = Shop.Render(Paid.AppendStage(PipelineStageDefinitionBuilder.Limit<BsonDocument>(2)));

        var listed = await ToList().ConfigureAwait(false);
        _many.Verify(Orders, paid, listed.Count, 10);

        var single = await Single().ConfigureAwait(false);
        _one.Verify(Orders, limited, single is null ? 0 : 1, 1);

        var streamed = await Stream().ConfigureAwait(false);
        _many.Verify(Orders, paid, streamed, 10);
    }
}

/// <summary>A database whose collections are the driver's to build and memory's to answer.</summary>
public sealed class Shop
{
    private readonly IMongoDatabase _driver;

    private readonly List<BsonDocument> _documents;

    private readonly IMongoCollection<BsonDocument> _collection;

    private string? _named;

    private List<BsonDocument> _rendered = [];

    /// <summary>A database that builds its collections with <paramref name="driver"/> and yields <paramref name="documents"/> orders.</summary>
    /// <param name="driver">The driver's database, which builds each collection asked for.</param>
    /// <param name="documents">How many documents every aggregation yields.</param>
    public Shop(IMongoDatabase driver, int documents)
    {
        _driver = driver;
        _documents = [.. Enumerable.Range(0, documents).Select(static n => new BsonDocument { { "_id", n }, { "status", "paid" } })];
        _collection = Stub.Of<IMongoCollection<BsonDocument>>(Aggregate);
        Database = Stub.Of<IMongoDatabase>(Collection);
    }

    /// <summary>The database to hand to the helpers.</summary>
    public IMongoDatabase Database { get; }

    /// <summary>A pipeline as the driver renders it for a collection of <see cref="BsonDocument"/>.</summary>
    /// <param name="pipeline">The pipeline.</param>
    /// <returns>The stages as BSON documents.</returns>
    public static List<BsonDocument> Render(PipelineDefinition<BsonDocument, BsonDocument> pipeline)
    {
        var rendered = pipeline.Render(new RenderArgs<BsonDocument>(BsonDocumentSerializer.Instance, BsonSerializer.SerializerRegistry));

        return [.. rendered.Documents];
    }

    /// <summary>Fails the run unless the last aggregation read <paramref name="name"/> with exactly <paramref name="pipeline"/>.</summary>
    /// <param name="name">The collection the helper should have asked for.</param>
    /// <param name="pipeline">The stages the driver would have sent, rendered.</param>
    /// <param name="yielded">How many documents the helper returned.</param>
    /// <param name="expected">How many it should have returned.</param>
    public void Verify(string name, List<BsonDocument> pipeline, int yielded, int expected)
    {
        var isSame = _rendered.Count == pipeline.Count
            && _rendered.Zip(pipeline).All(static stages => stages.First.ToBson().AsSpan().SequenceEqual(stages.Second.ToBson()));

        if (_named != name || !isSame || yielded != expected)
            throw new InvalidOperationException(
                $"The aggregation read '{_named}' with {_rendered.ToJson()} and yielded {yielded}; " +
                $"expected '{name}' with {pipeline.ToJson()} and {expected}.");
    }

    private object? Collection(MethodInfo method, object?[] args)
    {
        if (method.Name != nameof(IMongoDatabase.GetCollection) || method.GetGenericArguments()[0] != typeof(BsonDocument))
            throw new NotSupportedException(method.Name);

        method.Invoke(_driver, args);
        _named = (string?)args[0];

        return _collection;
    }

    private object? Aggregate(MethodInfo method, object?[] args)
    {
        if (method.Name != nameof(IMongoCollection<>.AggregateAsync))
            throw new NotSupportedException(method.Name);

        _rendered = Render((PipelineDefinition<BsonDocument, BsonDocument>)args[^3]!);

        return Task.FromResult<IAsyncCursor<BsonDocument>>(new Batch<BsonDocument>(_documents));
    }
}
