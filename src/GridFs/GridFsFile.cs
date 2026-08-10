namespace Snail.Toolkit.Mongodb.GridFs;

/// <summary>
/// The result of a GridFS lookup: the file that was found, together with the content type
/// read from its metadata.
/// </summary>
/// <param name="File">The file information.</param>
/// <param name="ContentType">
/// The content type taken from the file metadata, or <see langword="null"/> when the file carries
/// no such entry or the entry is not a string. A missing content type does not mean the file is missing.
/// </param>
public sealed record GridFsFile(GridFSFileInfo<ObjectId> File, string? ContentType)
{
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
}
