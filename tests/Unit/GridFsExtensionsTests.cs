using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using MongoDB.Driver.GridFS;

namespace Snail.Toolkit.Mongodb.Tests.Unit;

/// <summary>
/// Argument guards and failure behaviour, both observable without a server. Everything that needs
/// stored files is covered in <c>Integration.GridFsExtensionsIntegrationTests</c> against a real bucket.
/// </summary>
public class GridFsExtensionsTests
{
    private static IGridFSBucket Bucket(string connection = Given.Connection) =>
        Given.Database(connection).GridFs();

    [Fact]
    public async Task Lookups_WhenBucketIsNull_ThrowArgumentNullException()
    {
        IGridFSBucket bucket = null!;
        var id = ObjectId.GenerateNewId();

        await Assert.ThrowsAsync<ArgumentNullException>(() => bucket.FindOneByObjectIdAsync(id));
        await Assert.ThrowsAsync<ArgumentNullException>(() => bucket.FindOneByObjectIdAsync(id.ToString()));
        await Assert.ThrowsAsync<ArgumentNullException>(() => bucket.OpenDownloadByObjectIdAsync(id));
        await Assert.ThrowsAsync<ArgumentNullException>(() => bucket.OpenDownloadByObjectIdAsync(id.ToString()));
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => bucket.FindOneAsync(Builders<GridFSFileInfo<ObjectId>>.Filter.Empty));
    }

    [Fact]
    public async Task FindOneAsync_WhenFilterIsNull_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => Bucket().FindOneAsync(null!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-objectid")]
    [InlineData("deadbeef")]
    public async Task Lookups_WhenIdIsMalformed_ThrowArgumentException(string objectId)
    {
        var bucket = Bucket();

        var find = await Assert.ThrowsAsync<ArgumentException>(
            () => bucket.FindOneByObjectIdAsync(objectId));
        Assert.Equal("objectId", find.ParamName);

        var download = await Assert.ThrowsAsync<ArgumentException>(
            () => bucket.OpenDownloadByObjectIdAsync(objectId));
        Assert.Equal("objectId", download.ParamName);
    }

    [Fact]
    public async Task Lookups_WhenContentTypeKeyIsBlank_ThrowArgumentException()
    {
        var bucket = Bucket();

        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => bucket.FindOneAsync(
                Builders<GridFSFileInfo<ObjectId>>.Filter.Empty, contentTypeKey: " "));

        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => bucket.OpenDownloadByObjectIdAsync(ObjectId.GenerateNewId(), contentTypeKey: " "));
    }

    /// <summary>
    /// A transport failure must not be reported as "no such file" — the old implementation returned
    /// a plain <c>false</c> here and swallowed the exception entirely.
    /// </summary>
    [Fact]
    public async Task FindOneByObjectIdAsync_WhenTheServerIsUnreachable_Throws()
    {
        var exception = await Record.ExceptionAsync(
            () => Bucket(Given.UnreachableConnection).FindOneByObjectIdAsync(ObjectId.GenerateNewId()));

        Assert.NotNull(exception);
    }

    [Fact]
    public async Task OpenDownloadByObjectIdAsync_WhenTheServerIsUnreachable_Throws()
    {
        var exception = await Record.ExceptionAsync(
            () => Bucket(Given.UnreachableConnection).OpenDownloadByObjectIdAsync(ObjectId.GenerateNewId()));

        Assert.NotNull(exception);
    }

    [Fact]
    public void GridFs_WhenDatabaseIsNull_ThrowsArgumentNullException()
    {
        IMongoDatabase database = null!;

        Assert.Throws<ArgumentNullException>(() => database.GridFs());
    }

    [Fact]
    public void GridFs_HonoursTheGivenBucketOptions()
    {
        var bucket = Given.Database().GridFs(new GridFSBucketOptions
        {
            BucketName = "attachments",
            ChunkSizeBytes = 1024
        });

        Assert.Equal("attachments", bucket.Options.BucketName);
        Assert.Equal(1024, bucket.Options.ChunkSizeBytes);
    }

    [Fact]
    public async Task FindOneByObjectIdAsync_SendsTheFilterTheDriverRendersForTheId()
    {
        var id = ObjectId.GenerateNewId();
        var serializer = BsonSerializer.LookupSerializer<GridFSFileInfo<ObjectId>>();
        var arguments = new RenderArgs<GridFSFileInfo<ObjectId>>(serializer, BsonSerializer.SerializerRegistry);
        BsonDocument? sent = null;

        var bucket = Stub.Of<IGridFSBucket>((method, args) =>
        {
            Assert.Equal(nameof(IGridFSBucket.FindAsync), method.Name);
            sent = ((FilterDefinition<GridFSFileInfo<ObjectId>>)args[0]!).Render(arguments);

            return Task.FromResult<IAsyncCursor<GridFSFileInfo<ObjectId>>>(new NoFiles());
        });

        var found = await bucket.FindOneByObjectIdAsync(id);

        var expected = Builders<GridFSFileInfo<ObjectId>>.Filter.Eq(x => x.Id, id).Render(arguments);
        Assert.Null(found);
        Assert.Equal(expected.ToBson(), sent!.ToBson());
    }

    /// <summary>A file lookup that matched nothing.</summary>
    private sealed class NoFiles : IAsyncCursor<GridFSFileInfo<ObjectId>>
    {
        public IEnumerable<GridFSFileInfo<ObjectId>> Current => [];

        public bool MoveNext(CancellationToken cancellationToken = default) => false;

        public Task<bool> MoveNextAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);

        public void Dispose()
        {
        }
    }
}
