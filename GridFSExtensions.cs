namespace Toolkit.Mongo;

/// <summary>
/// Enter description here
/// </summary>
public static class GridFSExtensions
{
    /// <summary>
    /// Returns the first document of the cursor.
    /// </summary>
    /// <param name="bucket">Represents a GridFS system bucket.</param>
    /// <param name="filterDefinitionBuilder">The filter.</param>
    /// <param name="file">The file.</param>
    /// <param name="contentType">The contentType.</param>
    /// <param name="options">The options</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The task, the result of which is the value of the presence of the content of the document</returns>
    public static Task<bool> TryFindOneAsync(
        this IGridFSBucket bucket,
        FilterDefinition<GridFSFileInfo<ObjectId>> filterDefinitionBuilder, 
        out Task<GridFSFileInfo<ObjectId>> file,
        out Task<string> contentType, 
        GridFSFindOptions<ObjectId>? options = null,
        CancellationToken cancellationToken = default)
    {
        var filetTcs = new TaskCompletionSource<GridFSFileInfo<ObjectId>>();
        file = filetTcs.Task;
        
        var contentTypeTcs = new TaskCompletionSource<string>();
        contentType = contentTypeTcs.Task;
        
        return ((Func<Task<bool>>) (async () =>
        {
            var fileInfos = await bucket.FindAsync(
                filterDefinitionBuilder, 
                options, 
                cancellationToken);
            
            var fileInfo = await fileInfos.FirstOrDefaultAsync(cancellationToken);
            
            if (fileInfo is null) return false;
            
            filetTcs.SetResult(fileInfo);
            
            var metadataContentType = fileInfo.Metadata?.GetValue("ContentType", BsonNull.Value);
            
            if (metadataContentType is null || metadataContentType.IsBsonNull) return false;
            
            contentTypeTcs.SetResult(metadataContentType.AsString);
            return true;
        }))();
    }
    
    /// <summary>
    /// Returns the first document of the cursor.
    /// </summary>
    /// <param name="bucket">Represents a GridFS system bucket.</param>
    /// <param name="objectId">The objectId.</param>
    /// <param name="file">The file.</param>
    /// <param name="contentType">The contentType.</param>
    /// <param name="options">The options</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The task, the result of which is the value of the presence of the content of the document</returns>
    public static Task<bool> TryFindOneByObjectIdAsync(
        this IGridFSBucket bucket,
        string objectId,
        out Task<GridFSFileInfo<ObjectId>> file,
        out Task<string> contentType,
        GridFSFindOptions<ObjectId>? options = null,
        CancellationToken cancellationToken = default)
    {
        var filter = Builders<GridFSFileInfo<ObjectId>>.Filter.Eq(_ => _.Id, ObjectId.Parse(objectId));
        return bucket.TryFindOneAsync(
            filter, 
            out file,
            out contentType,
            options,
            cancellationToken).ContinueWith(
            t => !t.IsFaulted, 
            default, 
            TaskContinuationOptions.None, 
            TaskScheduler.Default);
    }
}