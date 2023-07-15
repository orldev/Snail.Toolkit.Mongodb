using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.GridFS;

namespace Toolkit.Mongo;
/// <summary>
/// Enter description here
/// </summary>
public class MongoService: IMongoService
{
    /// <summary>
    /// Enter description here
    /// </summary>
    private readonly IMongoDatabase _database;
    
    /// <summary>
    /// Enter description here
    /// </summary>
    /// <param name="options"></param>
    public MongoService(Action<MongoOptions> options)
    {
        var config = new MongoOptions();
        options.Invoke(config);
        
        MongoClient client = new(config.Connection);
        _database = client.GetDatabase(config.Database);
    }
    
    /// <summary>
    /// Enter description here
    /// </summary>
    /// <param name="options"></param>
    public MongoService(MongoOptions options)
    {
        MongoClient client = new(options.Connection);
        _database = client.GetDatabase(options.Database);
    }
    
    /// <summary>
    /// Enter description here
    /// </summary>
    public IGridFSBucket GridFs => new GridFSBucket(_database);
    
    /// <summary>
    /// Enter description here
    /// </summary>
    /// <param name="pipeline"></param>
    /// <param name="table"></param>
    /// <returns></returns>
    public async Task<BsonDocument?> AggregateSingleAsync(
        PipelineDefinition<BsonDocument, BsonDocument> pipeline, 
        string table)
    {
        var cursor = await AggregateAsync(pipeline, table);
        return await cursor.SingleOrDefaultAsync();
    }
    
    /// <summary>
    /// Enter description here
    /// </summary>
    /// <param name="pipeline"></param>
    /// <param name="table"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public async Task<IAsyncCursor<T>> AggregateAsync<T>(PipelineDefinition<T, T> pipeline, string table) => 
        await _database.GetCollection<T>(table).AggregateAsync(pipeline);

    /// <summary>
    /// 
    /// </summary>
    /// <param name="table"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public IMongoCollection<T> GetCollection<T>(string? table = null) => 
        _database.GetCollection<T>(string.IsNullOrEmpty(table) ? typeof(T).Name : table);
}