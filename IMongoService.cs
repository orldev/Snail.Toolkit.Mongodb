global using MongoDB.Bson;
global using MongoDB.Driver;
global using MongoDB.Driver.GridFS;

namespace Toolkit.Mongo;

/// <summary>
/// Enter description here
/// </summary>
public interface IMongoService
{
    /// <summary>
    /// Initializes an instance of the <see cref="GridFSBucket" /> class.
    /// </summary>
    IGridFSBucket GridFs { get; }
    
    /// <summary>
    /// Returns the only document of a cursor, or a default value if the cursor contains no documents.
    /// This method throws an exception if the cursor contains more than one document.
    /// </summary>
    /// <param name="pipeline">The pipeline.</param>
    /// <param name="collectionName">The collection name.</param>
    /// <param name="options">The options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A Task whose result is the only document of a cursor, or a default value if the cursor contains no documents.</returns>
    Task<BsonDocument?> AggregateSingleAsync(
        PipelineDefinition<BsonDocument, BsonDocument> pipeline, 
        string collectionName, 
        AggregateOptions? options = null,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Runs an aggregation pipeline.
    /// </summary>
    /// <param name="pipeline">The pipeline.</param>
    /// <param name="collectionName">The collection name.</param>
    /// <param name="options">The options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <typeparam name="T">The type of the result.</typeparam>
    /// <returns>A Task whose result is a cursor.</returns>
    Task<IAsyncCursor<T>> AggregateAsync<T>(
        PipelineDefinition<T, T> pipeline, 
        string collectionName , 
        AggregateOptions? options = null,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets a collection.
    /// </summary>
    /// <param name="name">The name of the collection.</param>
    /// <typeparam name="T">The document type.</typeparam>
    /// <returns>An implementation of a collection.</returns>
    IMongoCollection<T> GetCollection<T>(string? name = null);

    /// <summary>
    /// A builder for a <see cref="FilterDefinition{T}"/>.
    /// </summary>
    /// <typeparam name="T">The type of the document.</typeparam>
    /// <returns>Gets an <see cref="FilterDefinitionBuilder{T}"/>.</returns>
    FilterDefinitionBuilder<T> Filter<T>() => Builders<T>.Filter;
    
    /// <summary>
    /// A builder for an <see cref="UpdateDefinition{T}"/>.
    /// </summary>
    /// <typeparam name="T">The type of the document.</typeparam>
    /// <returns>Gets an <see cref="UpdateDefinitionBuilder{T}"/>.</returns>
    UpdateDefinitionBuilder<T> Update<T>() => Builders<T>.Update;
    
    /// <summary>
    /// A builder for a <see cref="SortDefinition{T}"/>.
    /// </summary>
    /// <typeparam name="T">The type of the document.</typeparam>
    /// <returns>Gets an <see cref="SortDefinitionBuilder{T}"/>.</returns>
    SortDefinitionBuilder<T> Sort<T>() => Builders<T>.Sort;
}