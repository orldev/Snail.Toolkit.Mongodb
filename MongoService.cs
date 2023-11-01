namespace Toolkit.Mongo;
/// <summary>
/// Enter description here
/// </summary>
public class MongoService: IMongoService
{
    /// <summary>
    /// Represents a database in MongoDB.
    /// </summary>
    private readonly IMongoDatabase _database;
    
    /// <summary>
    /// Enter description here
    /// </summary>
    /// <param name="options">The options.</param>
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
    /// <param name="options">The options.</param>
    public MongoService(MongoOptions options)
    {
        MongoClient client = new(options.Connection);
        _database = client.GetDatabase(options.Database);
    }
    // TODO: Add class GridFSBucketOptions
    /// <summary>
    /// Initializes an instance of the <see cref="GridFSBucket" /> class.
    /// </summary>
    public IGridFSBucket GridFs => new GridFSBucket(_database);

    /// <summary>
    /// Returns the only document of a cursor, or a default value if the cursor contains no documents.
    /// This method throws an exception if the cursor contains more than one document.
    /// </summary>
    /// <param name="pipeline">The pipeline.</param>
    /// <param name="collectionName">The collection name.</param>
    /// <param name="options">The options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A Task whose result is the only document of a cursor, or a default value if the cursor contains no documents.</returns>
    public async Task<BsonDocument?> AggregateSingleAsync(
        PipelineDefinition<BsonDocument, BsonDocument> pipeline, 
        string collectionName,
        AggregateOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var cursor = await AggregateAsync(pipeline, collectionName, options, cancellationToken);
        return await cursor.SingleOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// Runs an aggregation pipeline.
    /// </summary>
    /// <param name="pipeline">The pipeline.</param>
    /// <param name="collectionName">The collection name.</param>
    /// <param name="options">The options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <typeparam name="T">The type of the result.</typeparam>
    /// <returns>A Task whose result is a cursor.</returns>
    public async Task<IAsyncCursor<T>> AggregateAsync<T>(
        PipelineDefinition<T, T> pipeline,
        string collectionName, 
        AggregateOptions? options = null,
        CancellationToken cancellationToken = default) => 
        await _database.GetCollection<T>(collectionName).AggregateAsync(pipeline, options, cancellationToken);

    /// <summary>
    /// Gets a collection.
    /// </summary>
    /// <param name="name">The name of the collection.</param>
    /// <typeparam name="T">The document type.</typeparam>
    /// <returns>An implementation of a collection.</returns>
    public IMongoCollection<T> GetCollection<T>(string? name = null) => 
        _database.GetCollection<T>(string.IsNullOrEmpty(name) ? typeof(T).Name : name);
}