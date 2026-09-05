namespace Snail.Toolkit.Mongodb.GridFs;

/// <summary>
/// Lookup and download helpers over <see cref="IGridFSBucket"/> that return the file and its
/// content type in one call.
/// </summary>
/// <remarks>
/// None of these take a session, and none can: <see cref="IGridFSBucket"/> in MongoDB.Driver 3.10
/// declares no overload of <c>FindAsync</c> or <c>OpenDownloadStreamAsync</c> that accepts one, so a
/// GridFS read cannot join the transaction opened by <c>InTransactionAsync</c>. The aggregation
/// helpers on <see cref="IMongoDatabase"/> can.
/// </remarks>
public static class GridFsExtensions
{
    /// <summary>
    /// The metadata key holding the content type of a file.
    /// </summary>
    public const string DefaultContentTypeKey = "ContentType";

    /// <summary>
    /// Returns the first file matching a filter, without fetching its content.
    /// </summary>
    /// <param name="bucket">Represents a GridFS system bucket.</param>
    /// <param name="filter">The filter.</param>
    /// <param name="options">The options.</param>
    /// <param name="contentTypeKey">The metadata key holding the content type.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// A task whose result is the matching file, or <see langword="null"/> when nothing matched.
    /// Driver failures — an unreachable server, a timeout — surface as exceptions rather than as a miss.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="bucket"/> or <paramref name="filter"/> is null.</exception>
    public static async Task<GridFsFile?> FindOneAsync(
        this IGridFSBucket bucket,
        FilterDefinition<GridFSFileInfo<ObjectId>> filter,
        GridFSFindOptions<ObjectId>? options = null,
        string contentTypeKey = DefaultContentTypeKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bucket);
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentTypeKey);

        using var cursor = await bucket.FindAsync(filter, options, cancellationToken).ConfigureAwait(false);
        var fileInfo = await cursor.FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        return fileInfo is null
            ? null
            : new GridFsFile(fileInfo, ReadContentType(fileInfo, contentTypeKey));
    }

    /// <summary>
    /// Returns the file with the given identifier, without fetching its content.
    /// </summary>
    /// <param name="bucket">Represents a GridFS system bucket.</param>
    /// <param name="objectId">The identifier of the file.</param>
    /// <param name="options">The options.</param>
    /// <param name="contentTypeKey">The metadata key holding the content type.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task whose result is the file, or <see langword="null"/> when no such file exists.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="bucket"/> is null.</exception>
    public static Task<GridFsFile?> FindOneByObjectIdAsync(
        this IGridFSBucket bucket,
        ObjectId objectId,
        GridFSFindOptions<ObjectId>? options = null,
        string contentTypeKey = DefaultContentTypeKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bucket);

        var filter = Builders<GridFSFileInfo<ObjectId>>.Filter.Eq(x => x.Id, objectId);
        return bucket.FindOneAsync(filter, options, contentTypeKey, cancellationToken);
    }

    /// <summary>
    /// Returns the file with the given identifier, without fetching its content.
    /// </summary>
    /// <param name="bucket">Represents a GridFS system bucket.</param>
    /// <param name="objectId">The identifier of the file, as a 24 digit hex string.</param>
    /// <param name="options">The options.</param>
    /// <param name="contentTypeKey">The metadata key holding the content type.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task whose result is the file, or <see langword="null"/> when no such file exists.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="bucket"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="objectId"/> is not a valid <see cref="ObjectId"/>. Callers handling untrusted
    /// input should use <see cref="ObjectId.TryParse"/> and the <see cref="ObjectId"/> overload instead.
    /// </exception>
    public static Task<GridFsFile?> FindOneByObjectIdAsync(
        this IGridFSBucket bucket,
        string objectId,
        GridFSFindOptions<ObjectId>? options = null,
        string contentTypeKey = DefaultContentTypeKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bucket);

        return bucket.FindOneByObjectIdAsync(Parse(objectId), options, contentTypeKey, cancellationToken);
    }

    /// <summary>
    /// Opens the content of a file together with its metadata, in a single round trip.
    /// </summary>
    /// <remarks>
    /// This is the shape most web handlers want: one call gives the stream to copy to the response
    /// and the content type to label it with, instead of a lookup followed by a separate download.
    /// </remarks>
    /// <param name="bucket">Represents a GridFS system bucket.</param>
    /// <param name="objectId">The identifier of the file.</param>
    /// <param name="options">The options.</param>
    /// <param name="contentTypeKey">The metadata key holding the content type.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// A task whose result is the open download, which the caller must dispose, or
    /// <see langword="null"/> when no such file exists.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="bucket"/> is null.</exception>
    public static async Task<GridFsDownload?> OpenDownloadByObjectIdAsync(
        this IGridFSBucket bucket,
        ObjectId objectId,
        GridFSDownloadOptions? options = null,
        string contentTypeKey = DefaultContentTypeKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bucket);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentTypeKey);

        GridFSDownloadStream<ObjectId> stream;

        try
        {
            stream = await bucket.OpenDownloadStreamAsync(objectId, options, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (GridFSFileNotFoundException)
        {
            return null;
        }

        return new GridFsDownload(stream, ReadContentType(stream.FileInfo, contentTypeKey));
    }

    /// <summary>
    /// Opens the content of a file together with its metadata, in a single round trip.
    /// </summary>
    /// <param name="bucket">Represents a GridFS system bucket.</param>
    /// <param name="objectId">The identifier of the file, as a 24 digit hex string.</param>
    /// <param name="options">The options.</param>
    /// <param name="contentTypeKey">The metadata key holding the content type.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// A task whose result is the open download, which the caller must dispose, or
    /// <see langword="null"/> when no such file exists.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="bucket"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="objectId"/> is not a valid <see cref="ObjectId"/>.</exception>
    public static Task<GridFsDownload?> OpenDownloadByObjectIdAsync(
        this IGridFSBucket bucket,
        string objectId,
        GridFSDownloadOptions? options = null,
        string contentTypeKey = DefaultContentTypeKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bucket);

        return bucket.OpenDownloadByObjectIdAsync(Parse(objectId), options, contentTypeKey, cancellationToken);
    }

    private static ObjectId Parse(string objectId) =>
        ObjectId.TryParse(objectId, out var id)
            ? id
            : throw new ArgumentException(
                $"'{objectId}' is not a valid ObjectId; a 24 digit hex string is expected.", nameof(objectId));

    private static string? ReadContentType(GridFSFileInfo<ObjectId> fileInfo, string contentTypeKey)
    {
        if (fileInfo.Metadata is null)
            return null;

        return fileInfo.Metadata.TryGetValue(contentTypeKey, out var value) && value.IsString
            ? value.AsString
            : null;
    }
}
