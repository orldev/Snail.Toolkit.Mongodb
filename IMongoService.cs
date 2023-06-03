using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.GridFS;

namespace Toolkit.Mongo;
/// <summary>
/// Enter description here
/// </summary>
public interface IMongoService
{
    /// <summary>
    /// Enter description here
    /// </summary>
    IGridFSBucket GridFs { get; }
    /// <summary>
    /// Enter description here
    /// </summary>
    /// <param name="pipeline"></param>
    /// <param name="table"></param>
    /// <returns></returns>
    Task<BsonDocument?> AggregateSingleAsync(PipelineDefinition<BsonDocument, BsonDocument> pipeline, string table);
    
    /// <summary>
    /// Enter description here
    /// </summary>
    /// <param name="pipeline"></param>
    /// <param name="table"></param>
    Task<IAsyncCursor<T>> AggregateAsync<T>(PipelineDefinition<T, T> pipeline, string table);
    
    /// <summary>
    /// Enter description here
    /// </summary>
    /// <param name="table"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    IMongoCollection<T> GetCollection<T>(string? table = null);

    /// <summary>
    /// Enter description here
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    FilterDefinitionBuilder<T> Filter<T>() => Builders<T>.Filter;
    
    /// <summary>
    /// Enter description here
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    UpdateDefinitionBuilder<T> Update<T>() => Builders<T>.Update;
    
    /// <summary>
    /// Enter description here
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    SortDefinitionBuilder<T> Sort<T>() => Builders<T>.Sort;
}