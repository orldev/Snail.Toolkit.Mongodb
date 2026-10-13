using System.Reflection;
using BenchmarkDotNet.Attributes;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using MongoDB.Driver.GridFS;
using Snail.Toolkit.Mongodb.Extensions;
using Snail.Toolkit.Mongodb.GridFs;

namespace Snail.Toolkit.Mongodb.Benchmarks;

/// <summary>A GridFS file looked up by its identifier, as a download handler does per request, and a bucket built.</summary>
/// <remarks>
/// For the lookup the bucket stands in for the driver's: it renders the filter to BSON with the file serializer, as
/// the driver's bucket does before sending the find, and answers with one file from memory. The bucket built is the
/// driver's, over a client that has nothing to connect to.
/// </remarks>
[MemoryDiagnoser]
public class GridFsBenchmarks
{
    private static readonly IBsonSerializer<GridFSFileInfo<ObjectId>> Files =
        BsonSerializer.LookupSerializer<GridFSFileInfo<ObjectId>>();

    private readonly ObjectId _id = ObjectId.Parse("6ace05650b2cc770cf200ec8");

    private MongoClient _client = null!;

    private IMongoDatabase _database = null!;

    private List<GridFSFileInfo<ObjectId>> _files = null!;

    private IGridFSBucket _bucket = null!;

    private BsonDocument _rendered = [];

    /// <summary>A bucket holding one PDF.</summary>
    /// <returns>A task that completes once the lookup was checked against the filter the driver would send.</returns>
    [GlobalSetup]
    public async Task Setup()
    {
        var file = new BsonDocument
        {
            { "_id", _id },
            { "length", 5L },
            { "chunkSize", 261_120 },
            { "uploadDate", new DateTime(2026, 10, 13, 0, 0, 0, DateTimeKind.Utc) },
            { "filename", "invoice.pdf" },
            { "metadata", new BsonDocument(GridFsExtensions.DefaultContentTypeKey, "application/pdf") }
        };

        _client = new MongoClient("mongodb://127.0.0.1:1/?serverSelectionTimeoutMS=100");
        _database = _client.GetDatabase("shop");
        _files = [BsonSerializer.Deserialize<GridFSFileInfo<ObjectId>>(file)];
        _bucket = Stub.Of<IGridFSBucket>(Find);

        await VerifyAsync().ConfigureAwait(false);
    }

    /// <summary>The file and its content type, by identifier.</summary>
    /// <returns>The file.</returns>
    [Benchmark(Description = "FindOneByObjectIdAsync(ObjectId)")]
    public Task<GridFsFile?> FindById() => _bucket.FindOneByObjectIdAsync(_id);

    /// <summary>Releases the client.</summary>
    [GlobalCleanup]
    public void Cleanup() => _client.Dispose();

    /// <summary>A bucket over a database, as a caller without DI gets one.</summary>
    /// <returns>The bucket.</returns>
    [Benchmark(Description = "GridFs()")]
    public IGridFSBucket Bucket() => _database.GridFs();

    /// <summary>Fails the run unless the lookup sends <c>{ _id: id }</c> and reads the file with its content type.</summary>
    /// <returns>A task that completes once the lookup was checked.</returns>
    private async Task VerifyAsync()
    {
        var found = await FindById().ConfigureAwait(false);
        var expected = new BsonDocument("_id", _id);

        if (found is null || found.Id != _id || found.ContentType != "application/pdf"
            || !_rendered.ToBson().AsSpan().SequenceEqual(expected.ToBson()))
            throw new InvalidOperationException($"The lookup sent {_rendered.ToJson()} and found {found?.Id}.");
    }

    private object? Find(MethodInfo method, object?[] args)
    {
        if (method.Name != nameof(IGridFSBucket.FindAsync))
            throw new NotSupportedException(method.Name);

        var filter = (FilterDefinition<GridFSFileInfo<ObjectId>>)args[0]!;
        _rendered = filter.Render(new RenderArgs<GridFSFileInfo<ObjectId>>(Files, BsonSerializer.SerializerRegistry));

        return Task.FromResult<IAsyncCursor<GridFSFileInfo<ObjectId>>>(new Batch<GridFSFileInfo<ObjectId>>(_files));
    }
}
