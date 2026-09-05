using System.Text;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.GridFS;

namespace Snail.Toolkit.Mongodb.Tests.Integration;

[Collection(MongoContainerCollection.Name)]
public class GridFsExtensionsIntegrationTests(MongoContainerFixture fixture)
{
    private static readonly byte[] Content = "hello gridfs"u8.ToArray();

    private static Task<ObjectId> Upload(
        IGridFSBucket bucket, BsonDocument? metadata = null, string name = "file.txt") =>
        bucket.UploadFromBytesAsync(name, Content, new GridFSUploadOptions { Metadata = metadata });

    [Fact]
    public async Task FindOneByObjectIdAsync_ReturnsTheUploadedFileAndItsContentType()
    {
        var db = fixture.CreateDatabase();
        var id = await Upload(db.GridFs(), new BsonDocument("ContentType", "text/plain"));

        var found = await db.GridFs().FindOneByObjectIdAsync(id);

        Assert.NotNull(found);
        Assert.Equal(id, found.Id);
        Assert.Equal("file.txt", found.Filename);
        Assert.Equal(Content.Length, found.Length);
        Assert.Equal("text/plain", found.ContentType);
    }

    [Fact]
    public async Task FindOneByObjectIdAsync_AcceptsTheIdAsAString()
    {
        var db = fixture.CreateDatabase();
        var id = await Upload(db.GridFs(), new BsonDocument("ContentType", "text/plain"));

        var found = await db.GridFs().FindOneByObjectIdAsync(id.ToString());

        Assert.Equal("text/plain", found?.ContentType);
    }

    [Fact]
    public async Task FindOneByObjectIdAsync_WhenTheFileDoesNotExist_ReturnsNull()
    {
        var db = fixture.CreateDatabase();
        await Upload(db.GridFs(), new BsonDocument("ContentType", "text/plain"));

        Assert.Null(await db.GridFs().FindOneByObjectIdAsync(ObjectId.GenerateNewId()));
    }

    /// <summary>
    /// A file with no content type is still a file — the old implementation reported it as a miss
    /// and left the caller awaiting a task that never completed.
    /// </summary>
    [Fact]
    public async Task FindOneByObjectIdAsync_WhenTheFileHasNoMetadata_StillReturnsIt()
    {
        var db = fixture.CreateDatabase();
        var id = await Upload(db.GridFs());

        var found = await db.GridFs().FindOneByObjectIdAsync(id);

        Assert.NotNull(found);
        Assert.Equal(id, found.Id);
        Assert.Null(found.ContentType);
    }

    [Fact]
    public async Task FindOneByObjectIdAsync_WhenContentTypeIsNull_StillReturnsTheFile()
    {
        var db = fixture.CreateDatabase();
        var id = await Upload(db.GridFs(), new BsonDocument("ContentType", BsonNull.Value));

        var found = await db.GridFs().FindOneByObjectIdAsync(id);

        Assert.NotNull(found);
        Assert.Null(found.ContentType);
    }

    [Fact]
    public async Task FindOneByObjectIdAsync_WhenMetadataHasNoContentTypeKey_StillReturnsTheFile()
    {
        var db = fixture.CreateDatabase();
        var id = await Upload(db.GridFs(), new BsonDocument("Author", "orldev"));

        var found = await db.GridFs().FindOneByObjectIdAsync(id);

        Assert.NotNull(found);
        Assert.Equal(id, found.Id);
        Assert.Null(found.ContentType);
    }

    /// <summary>
    /// Reading a non-string metadata value used to throw from <c>AsString</c>.
    /// </summary>
    [Fact]
    public async Task FindOneByObjectIdAsync_WhenContentTypeIsNotAString_StillReturnsTheFile()
    {
        var db = fixture.CreateDatabase();
        var id = await Upload(db.GridFs(), new BsonDocument("ContentType", 42));

        var found = await db.GridFs().FindOneByObjectIdAsync(id);

        Assert.NotNull(found);
        Assert.Equal(id, found.Id);
        Assert.Null(found.ContentType);
    }

    [Fact]
    public async Task FindOneByObjectIdAsync_ReadsACustomMetadataKey()
    {
        var db = fixture.CreateDatabase();
        var id = await Upload(db.GridFs(), new BsonDocument("mime", "application/pdf"));

        var found = await db.GridFs().FindOneByObjectIdAsync(id, contentTypeKey: "mime");

        Assert.Equal("application/pdf", found?.ContentType);
    }

    [Fact]
    public async Task FindOneAsync_ReturnsTheFirstMatchOfAFilter()
    {
        var db = fixture.CreateDatabase();
        await Upload(db.GridFs(), name: "a.txt");
        var wanted = await Upload(db.GridFs(), new BsonDocument("ContentType", "text/plain"), "b.txt");

        var found = await db.GridFs().FindOneAsync(
            Builders<GridFSFileInfo<ObjectId>>.Filter.Eq(x => x.Filename, "b.txt"));

        Assert.Equal(wanted, found?.Id);
    }

    [Fact]
    public async Task FindOneAsync_WhenNothingMatches_ReturnsNull()
    {
        var db = fixture.CreateDatabase();
        await Upload(db.GridFs());

        var found = await db.GridFs().FindOneAsync(
            Builders<GridFSFileInfo<ObjectId>>.Filter.Eq(x => x.Filename, "missing.txt"));

        Assert.Null(found);
    }

    [Fact]
    public async Task FindOneByObjectIdAsync_HonoursCancellation()
    {
        var db = fixture.CreateDatabase();
        var id = await Upload(db.GridFs());

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => db.GridFs().FindOneByObjectIdAsync(id, cancellationToken: cts.Token));
    }

    [Fact]
    public async Task GridFs_UsesTheConfiguredBucketName()
    {
        var db = fixture.CreateDatabase();
        var bucket = db.GridFs(new GridFSBucketOptions { BucketName = "attachments" });

        await Upload(bucket, new BsonDocument("ContentType", "text/plain"));

        var names = await (await db.ListCollectionNamesAsync()).ToListAsync();

        Assert.Contains("attachments.files", names);
        Assert.DoesNotContain("fs.files", names);
    }

    /// <summary>
    /// The shape a web handler wants: content and content type from a single round trip.
    /// </summary>
    [Fact]
    public async Task OpenDownloadByObjectIdAsync_ReturnsTheContentAndTheContentType()
    {
        var db = fixture.CreateDatabase();
        var id = await Upload(db.GridFs(), new BsonDocument("ContentType", "text/plain"));

        await using var download = await db.GridFs().OpenDownloadByObjectIdAsync(id);

        Assert.NotNull(download);
        Assert.Equal(id, download.Id);
        Assert.Equal("file.txt", download.Filename);
        Assert.Equal("text/plain", download.ContentType);

        using var reader = new StreamReader(download.Stream, Encoding.UTF8);
        Assert.Equal("hello gridfs", await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task OpenDownloadByObjectIdAsync_AcceptsTheIdAsAString()
    {
        var db = fixture.CreateDatabase();
        var id = await Upload(db.GridFs(), new BsonDocument("ContentType", "text/plain"));

        await using var download = await db.GridFs().OpenDownloadByObjectIdAsync(id.ToString());

        Assert.Equal("text/plain", download?.ContentType);
    }

    [Fact]
    public async Task OpenDownloadByObjectIdAsync_WhenTheFileDoesNotExist_ReturnsNull()
    {
        var db = fixture.CreateDatabase();
        await Upload(db.GridFs());

        var download = await db.GridFs().OpenDownloadByObjectIdAsync(ObjectId.GenerateNewId());

        Assert.Null(download);
    }

    [Fact]
    public async Task OpenDownloadByObjectIdAsync_WhenTheFileHasNoMetadata_StillReturnsTheContent()
    {
        var db = fixture.CreateDatabase();
        var id = await Upload(db.GridFs());

        await using var download = await db.GridFs().OpenDownloadByObjectIdAsync(id);

        Assert.NotNull(download);
        Assert.Null(download.ContentType);
        Assert.Equal(Content.Length, download.Length);
    }

    [Fact]
    public async Task OpenDownloadByObjectIdAsync_ReadsACustomMetadataKey()
    {
        var db = fixture.CreateDatabase();
        var id = await Upload(db.GridFs(), new BsonDocument("mime", "application/pdf"));

        await using var download = await db.GridFs().OpenDownloadByObjectIdAsync(id, contentTypeKey: "mime");

        Assert.Equal("application/pdf", download?.ContentType);
    }
}
