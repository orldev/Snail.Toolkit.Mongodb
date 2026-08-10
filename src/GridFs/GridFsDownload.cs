namespace Snail.Toolkit.Mongodb.GridFs;

/// <summary>
/// An open GridFS download: the content stream plus the metadata that came with it, fetched in a
/// single round trip.
/// </summary>
/// <remarks>
/// Owns the stream — dispose it, preferably with <c>await using</c>.
/// </remarks>
public sealed class GridFsDownload : IDisposable, IAsyncDisposable
{
    private readonly GridFSDownloadStream<ObjectId> _stream;

    internal GridFsDownload(GridFSDownloadStream<ObjectId> stream, string? contentType)
    {
        _stream = stream;
        ContentType = contentType;
    }

    /// <summary>
    /// The file information the server sent along with the stream.
    /// </summary>
    public GridFSFileInfo<ObjectId> File => _stream.FileInfo;

    /// <summary>
    /// The content type taken from the file metadata, or <see langword="null"/> when the file
    /// carries no such entry or the entry is not a string.
    /// </summary>
    public string? ContentType { get; }

    /// <summary>
    /// The content of the file. Readable, seekable, and owned by this instance.
    /// </summary>
    public Stream Stream => _stream;

    /// <summary>
    /// The identifier of the file.
    /// </summary>
    public ObjectId Id => File.Id;

    /// <summary>
    /// The length of the file in bytes.
    /// </summary>
    public long Length => File.Length;

    /// <summary>
    /// The name the file was stored under.
    /// </summary>
    public string Filename => File.Filename;

    /// <inheritdoc />
    public void Dispose() => _stream.Dispose();

    /// <inheritdoc />
    public ValueTask DisposeAsync() => _stream.DisposeAsync();
}
