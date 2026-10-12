using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Snail.Toolkit.Mongodb.Extensions;

/// <summary>
/// Collection access and aggregation helpers over the driver's own <see cref="IMongoDatabase"/>.
/// </summary>
/// <remarks>
/// These compose with the driver instead of wrapping it: nothing here has to be implemented by a
/// wrapper type, and a consumer can adopt one method without buying into anything else.
/// </remarks>
public static class MongoDatabaseExtensions
{
    /// <summary>
    /// Gets a collection, naming it after the document type when no name is given.
    /// </summary>
    /// <remarks>
    /// The driver builds a new collection object on every call — roughly 1.4 µs and 2.7 KB, measured
    /// on .NET 10 — and does not cache them. A returned collection is immutable and thread-safe, so
    /// hold it in a field rather than calling this per operation; a <c>Find</c> on a held collection
    /// costs about 33 ns by comparison. Resolving the name costs 4 ns for a plain type.
    ///
    /// <paramref name="settings"/> is here so that taking the naming convention does not cost the
    /// per-collection write concern the driver's own overload offers.
    ///
    /// A convention is caller-supplied and can return anything. When it returned nothing the driver
    /// complained about a parameter named <c>name</c> that this caller never passed, so an empty
    /// result is refused here instead, against the parameter that produced it.
    /// </remarks>
    /// <param name="database">The database.</param>
    /// <param name="name">The collection name. When null or empty, <paramref name="naming"/> supplies it.</param>
    /// <param name="naming">
    /// Maps the document type to a collection name. Defaults to <see cref="MongoNaming.ForType"/>,
    /// which uses the type name with generics unwrapped, so <c>Page&lt;Order&gt;</c> becomes <c>PageOfOrder</c>.
    /// </param>
    /// <param name="settings">Read and write concern, read preference and the rest of the driver's collection settings.</param>
    /// <typeparam name="T">The document type.</typeparam>
    /// <returns>An implementation of a collection.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="database"/> is null.</exception>
    public static IMongoCollection<T> GetCollection<T>(
        this IMongoDatabase database,
        string? name = null,
        Func<Type, string>? naming = null,
        MongoCollectionSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(database);

        if (string.IsNullOrEmpty(name) is false)
            return database.GetCollection<T>(name, settings);

        var resolved = (naming ?? MongoNaming.Default).Invoke(typeof(T));

        if (string.IsNullOrWhiteSpace(resolved))
            throw new ArgumentException(
                $"The naming convention produced no collection name for '{typeof(T).Name}'.", nameof(naming));

        return database.GetCollection<T>(resolved, settings);
    }

    /// <summary>
    /// Gets a GridFS bucket over this database.
    /// </summary>
    /// <remarks>
    /// Creates a bucket per call, which is cheap — it holds no connections of its own. Under DI,
    /// resolve <see cref="IGridFSBucket"/> instead and get the registered one.
    /// </remarks>
    /// <param name="database">The database.</param>
    /// <param name="options">Bucket options, such as bucket name or chunk size.</param>
    /// <returns>A bucket.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="database"/> is null.</exception>
    public static IGridFSBucket GridFs(this IMongoDatabase database, GridFSBucketOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(database);

        return new GridFSBucket(database, options);
    }

    /// <summary>
    /// Runs an aggregation pipeline and returns the raw cursor.
    /// </summary>
    /// <remarks>
    /// The caller owns the returned cursor and must dispose it. Prefer <c>AggregateToListAsync</c> or
    /// <c>AggregateAsAsyncEnumerable</c>, which handle that.
    /// </remarks>
    /// <param name="database">The database.</param>
    /// <param name="pipeline">The pipeline.</param>
    /// <param name="collectionName">The collection name.</param>
    /// <param name="options">The options.</param>
    /// <param name="session">The session to run inside, so the read joins its transaction.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task whose result is a cursor the caller must dispose.</returns>
    public static Task<IAsyncCursor<BsonDocument>> AggregateAsync(
        this IMongoDatabase database,
        PipelineDefinition<BsonDocument, BsonDocument> pipeline,
        string collectionName,
        AggregateOptions? options = null,
        IClientSessionHandle? session = null,
        CancellationToken cancellationToken = default) =>
        database.AggregateAsync<BsonDocument, BsonDocument>(pipeline, collectionName, options, session, cancellationToken);

    /// <summary>
    /// Runs an aggregation pipeline and returns the raw cursor.
    /// </summary>
    /// <param name="database">The database.</param>
    /// <param name="pipeline">The pipeline.</param>
    /// <param name="collectionName">The collection name.</param>
    /// <param name="options">The options.</param>
    /// <param name="session">The session to run inside, so the read joins its transaction.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <typeparam name="TInput">The type of the documents the pipeline reads.</typeparam>
    /// <typeparam name="TOutput">The type of the documents the pipeline yields.</typeparam>
    /// <returns>A task whose result is a cursor the caller must dispose.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="database"/> is null.</exception>
    public static async Task<IAsyncCursor<TOutput>> AggregateAsync<TInput, TOutput>(
        this IMongoDatabase database,
        PipelineDefinition<TInput, TOutput> pipeline,
        string collectionName,
        AggregateOptions? options = null,
        IClientSessionHandle? session = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);

        var collection = database.GetCollection<TInput>(collectionName);

        return await (session is null
                ? collection.AggregateAsync(pipeline, options, cancellationToken)
                : collection.AggregateAsync(session, pipeline, options, cancellationToken))
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Returns the only document of a pipeline, or <see langword="null"/> if it yields none.
    /// </summary>
    /// <param name="database">The database.</param>
    /// <param name="pipeline">The pipeline.</param>
    /// <param name="collectionName">The collection name.</param>
    /// <param name="options">The options.</param>
    /// <param name="session">The session to run inside, so the read joins its transaction.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task whose result is the only document, or <see langword="null"/>.</returns>
    /// <exception cref="InvalidOperationException">The pipeline yielded more than one document.</exception>
    public static Task<BsonDocument?> AggregateSingleAsync(
        this IMongoDatabase database,
        PipelineDefinition<BsonDocument, BsonDocument> pipeline,
        string collectionName,
        AggregateOptions? options = null,
        IClientSessionHandle? session = null,
        CancellationToken cancellationToken = default) =>
        database.AggregateSingleAsync<BsonDocument, BsonDocument>(pipeline, collectionName, options, session, cancellationToken);

    /// <summary>
    /// Returns the only document of a pipeline, or the default value if it yields none.
    /// </summary>
    /// <remarks>
    /// <typeparamref name="TInput"/> and <typeparamref name="TOutput"/> are separate because a
    /// pipeline reshapes documents: a <c>$project</c> or <c>$lookup</c> over a typed collection
    /// commonly yields a shape with no model of its own, which
    /// <c>PipelineDefinition&lt;Order, BsonDocument&gt;</c> expresses.
    ///
    /// A <c>$limit</c> of two is appended: deciding "more than one" needs a second document and no
    /// more, while the server would otherwise fill a whole batch — up to 101 documents or 16 MB — to
    /// answer a question about one. A pipeline ending in <c>$out</c> or <c>$merge</c> yields no
    /// documents and has no business here in the first place.
    /// </remarks>
    /// <param name="database">The database.</param>
    /// <param name="pipeline">The pipeline.</param>
    /// <param name="collectionName">The collection name.</param>
    /// <param name="options">The options.</param>
    /// <param name="session">The session to run inside, so the read joins its transaction.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <typeparam name="TInput">The type of the documents the pipeline reads.</typeparam>
    /// <typeparam name="TOutput">The type of the documents the pipeline yields.</typeparam>
    /// <returns>A task whose result is the only document, or the default value of <typeparamref name="TOutput"/>.</returns>
    /// <exception cref="InvalidOperationException">The pipeline yielded more than one document.</exception>
    public static async Task<TOutput?> AggregateSingleAsync<TInput, TOutput>(
        this IMongoDatabase database,
        PipelineDefinition<TInput, TOutput> pipeline,
        string collectionName,
        AggregateOptions? options = null,
        IClientSessionHandle? session = null,
        CancellationToken cancellationToken = default)
    {
        var limited = pipeline.AppendStage(PipelineStageDefinitionBuilder.Limit<TOutput>(2));

        using var cursor = await database.AggregateAsync(limited, collectionName, options, session, cancellationToken)
            .ConfigureAwait(false);

        return await cursor.SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs an aggregation pipeline and buffers every document, disposing the cursor.
    /// </summary>
    /// <param name="database">The database.</param>
    /// <param name="pipeline">The pipeline.</param>
    /// <param name="collectionName">The collection name.</param>
    /// <param name="options">The options.</param>
    /// <param name="session">The session to run inside, so the read joins its transaction.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task whose result is every document the pipeline yielded.</returns>
    public static Task<List<BsonDocument>> AggregateToListAsync(
        this IMongoDatabase database,
        PipelineDefinition<BsonDocument, BsonDocument> pipeline,
        string collectionName,
        AggregateOptions? options = null,
        IClientSessionHandle? session = null,
        CancellationToken cancellationToken = default) =>
        database.AggregateToListAsync<BsonDocument, BsonDocument>(pipeline, collectionName, options, session, cancellationToken);

    /// <summary>
    /// Runs an aggregation pipeline and buffers every document, disposing the cursor.
    /// </summary>
    /// <param name="database">The database.</param>
    /// <param name="pipeline">The pipeline.</param>
    /// <param name="collectionName">The collection name.</param>
    /// <param name="options">The options.</param>
    /// <param name="session">The session to run inside, so the read joins its transaction.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <typeparam name="TInput">The type of the documents the pipeline reads.</typeparam>
    /// <typeparam name="TOutput">The type of the documents the pipeline yields.</typeparam>
    /// <returns>A task whose result is every document the pipeline yielded.</returns>
    public static async Task<List<TOutput>> AggregateToListAsync<TInput, TOutput>(
        this IMongoDatabase database,
        PipelineDefinition<TInput, TOutput> pipeline,
        string collectionName,
        AggregateOptions? options = null,
        IClientSessionHandle? session = null,
        CancellationToken cancellationToken = default)
    {
        using var cursor = await database.AggregateAsync(pipeline, collectionName, options, session, cancellationToken)
            .ConfigureAwait(false);

        return await cursor.ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs an aggregation pipeline and streams the documents, disposing the cursor when enumeration
    /// ends — including when the consumer breaks out early.
    /// </summary>
    /// <param name="database">The database.</param>
    /// <param name="pipeline">The pipeline.</param>
    /// <param name="collectionName">The collection name.</param>
    /// <param name="options">The options.</param>
    /// <param name="session">The session to run inside, so the read joins its transaction.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The documents, streamed batch by batch.</returns>
    public static IAsyncEnumerable<BsonDocument> AggregateAsAsyncEnumerable(
        this IMongoDatabase database,
        PipelineDefinition<BsonDocument, BsonDocument> pipeline,
        string collectionName,
        AggregateOptions? options = null,
        IClientSessionHandle? session = null,
        CancellationToken cancellationToken = default) =>
        database.AggregateAsAsyncEnumerable<BsonDocument, BsonDocument>(
            pipeline, collectionName, options, session, cancellationToken);

    /// <summary>
    /// Runs an aggregation pipeline and streams the documents, disposing the cursor when enumeration
    /// ends — including when the consumer breaks out early.
    /// </summary>
    /// <param name="database">The database.</param>
    /// <param name="pipeline">The pipeline.</param>
    /// <param name="collectionName">The collection name.</param>
    /// <param name="options">The options.</param>
    /// <param name="session">The session to run inside, so the read joins its transaction.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <typeparam name="TInput">The type of the documents the pipeline reads.</typeparam>
    /// <typeparam name="TOutput">The type of the documents the pipeline yields.</typeparam>
    /// <returns>The documents, streamed batch by batch.</returns>
    [SuppressMessage("Style", "IDE1006", Justification = "Shipped in 1.0.0 without the Async suffix; a rename would break its users.")]
    public static async IAsyncEnumerable<TOutput> AggregateAsAsyncEnumerable<TInput, TOutput>(
        this IMongoDatabase database,
        PipelineDefinition<TInput, TOutput> pipeline,
        string collectionName,
        AggregateOptions? options = null,
        IClientSessionHandle? session = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var cursor = await database.AggregateAsync(pipeline, collectionName, options, session, cancellationToken)
            .ConfigureAwait(false);

        while (await cursor.MoveNextAsync(cancellationToken).ConfigureAwait(false))
            foreach (var document in cursor.Current)
                yield return document;
    }
}
