using System.Collections.Concurrent;
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
    private const int HeldNames = 256;

    private static readonly ConditionalWeakTable<IMongoDatabase, ConcurrentDictionary<Type, Conventional>> Conventionals = [];

    private static readonly ConditionalWeakTable<IMongoDatabase, ConcurrentDictionary<(Type Document, string Name), object>> Aggregated = [];

    /// <summary>
    /// Gets a collection, naming it after the document type when no name is given.
    /// </summary>
    /// <remarks>
    /// The driver builds a new collection object on every call — roughly 0.5–1.4 µs and 2.6 KB, measured
    /// on .NET 10 — and does not cache them. Called with nothing but the document type, this method keeps
    /// the collection per database and type (a collection is immutable and thread-safe), so a repository
    /// that asks for it per request pays a lookup and allocates nothing; the process-wide convention maps a
    /// type to one name, and a convention chosen later through <see cref="MongoNaming.Use"/> is followed.
    /// With a name, a convention or settings the driver builds a new collection every call: hold that one
    /// in a field rather than calling this per operation. Resolving the name costs 4 ns for a plain type.
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

        if (naming is null && settings is null)
            return ConventionalCollection<T>(database);

        return database.GetCollection<T>(Named<T>(naming ?? MongoNaming.Default), settings);
    }

    /// <summary>
    /// The collection named by the process-wide convention, built once per database, type and convention.
    /// </summary>
    /// <remarks>
    /// A database's table is written once per document type and read without a lock afterwards, so it takes one
    /// lock rather than the default of one per processor, which cost a caller that gets the database from the
    /// client per request some 600 bytes on every call.
    /// </remarks>
    /// <param name="database">The database.</param>
    /// <typeparam name="T">The document type.</typeparam>
    /// <returns>The held collection, or a new one when the type was not asked for yet or the convention changed.</returns>
    private static IMongoCollection<T> ConventionalCollection<T>(IMongoDatabase database)
    {
        var naming = MongoNaming.Default;
        var held = Conventionals.GetValue(database, static _ => new ConcurrentDictionary<Type, Conventional>(concurrencyLevel: 1, capacity: 1));

        if (held.TryGetValue(typeof(T), out var conventional) && conventional.Naming.Equals(naming))
            return (IMongoCollection<T>)conventional.Collection;

        var collection = database.GetCollection<T>(Named<T>(naming));

        held[typeof(T)] = new Conventional(naming, collection);

        return collection;
    }

    /// <summary>
    /// The named collection an aggregation reads, built once per database, document type and name.
    /// </summary>
    /// <remarks>
    /// The driver builds a new collection object on every <c>GetCollection</c> — 2.6 KB, the larger part of what
    /// an aggregation helper allocated before the first byte was sent. A collection is immutable and thread-safe, and
    /// one built without settings carries the database's, so the one built first serves every later call. Names are
    /// the caller's and may be made per tenant or per day: past <see cref="HeldNames"/> names a database's table
    /// stops growing and the driver builds the collection every call, as before. An empty name is the driver's to
    /// refuse, with the exception it always threw.
    /// </remarks>
    /// <param name="database">The database.</param>
    /// <param name="name">The collection name.</param>
    /// <typeparam name="T">The document type.</typeparam>
    /// <returns>The held collection, or a new one when the name was not asked for yet.</returns>
    private static IMongoCollection<T> AggregatedCollection<T>(IMongoDatabase database, string name)
    {
        if (string.IsNullOrEmpty(name))
            return database.GetCollection<T>(name);

        var held = Aggregated.GetValue(database, static _ => new ConcurrentDictionary<(Type, string), object>(concurrencyLevel: 1, capacity: 1));

        if (held.TryGetValue((typeof(T), name), out var kept))
            return (IMongoCollection<T>)kept;

        var collection = database.GetCollection<T>(name);

        if (held.Count < HeldNames)
            held.TryAdd((typeof(T), name), collection);

        return collection;
    }

    /// <summary>
    /// Asks the convention for the collection name, refusing an empty answer against the convention.
    /// </summary>
    /// <param name="naming">Maps the document type to a collection name.</param>
    /// <typeparam name="T">The document type.</typeparam>
    /// <returns>The collection name.</returns>
    /// <exception cref="ArgumentException">The convention produced no name.</exception>
    private static string Named<T>(Func<Type, string> naming)
    {
        var resolved = naming(typeof(T));

        if (string.IsNullOrWhiteSpace(resolved))
            throw new ArgumentException(
                $"The naming convention produced no collection name for '{typeof(T).Name}'.", nameof(naming));

        return resolved;
    }

    /// <summary>
    /// Gets a GridFS bucket over this database.
    /// </summary>
    /// <remarks>
    /// Creates a bucket per call. It holds no connections of its own, but building one costs some 0.7 µs and
    /// 4.7 KB, measured on .NET 10, and a new bucket checks the files collection's indexes again on its first
    /// upload: build it once and keep it. Under DI, resolve <see cref="IGridFSBucket"/> instead and get the
    /// registered one.
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
        return await Aggregate(database, pipeline, collectionName, options, session, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Starts an aggregation on the held collection and hands back the driver's task as it is.
    /// </summary>
    /// <remarks>
    /// Every caller is an <see langword="async"/> method that awaits the task at once, so an argument refused here
    /// surfaces from the caller's task exactly as it did through a second state machine, which cost one more task
    /// per call.
    /// </remarks>
    /// <param name="database">The database.</param>
    /// <param name="pipeline">The pipeline.</param>
    /// <param name="collectionName">The collection name.</param>
    /// <param name="options">The options.</param>
    /// <param name="session">The session to run inside, so the read joins its transaction.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <typeparam name="TInput">The type of the documents the pipeline reads.</typeparam>
    /// <typeparam name="TOutput">The type of the documents the pipeline yields.</typeparam>
    /// <returns>The driver's task, whose result is a cursor the caller must dispose.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="database"/> is null.</exception>
    private static Task<IAsyncCursor<TOutput>> Aggregate<TInput, TOutput>(
        IMongoDatabase database,
        PipelineDefinition<TInput, TOutput> pipeline,
        string collectionName,
        AggregateOptions? options,
        IClientSessionHandle? session,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(database);

        var collection = AggregatedCollection<TInput>(database, collectionName);

        return session is null
            ? collection.AggregateAsync(pipeline, options, cancellationToken)
            : collection.AggregateAsync(session, pipeline, options, cancellationToken);
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
        var limited = pipeline.AppendStage(Pair<TOutput>.Limit);

        using var cursor = await Aggregate(database, limited, collectionName, options, session, cancellationToken)
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
        using var cursor = await Aggregate(database, pipeline, collectionName, options, session, cancellationToken)
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
    public static async IAsyncEnumerable<TOutput> AggregateAsAsyncEnumerable<TInput, TOutput>(
        this IMongoDatabase database,
        PipelineDefinition<TInput, TOutput> pipeline,
        string collectionName,
        AggregateOptions? options = null,
        IClientSessionHandle? session = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var cursor = await Aggregate(database, pipeline, collectionName, options, session, cancellationToken)
            .ConfigureAwait(false);

        while (await cursor.MoveNextAsync(cancellationToken).ConfigureAwait(false))
            foreach (var document in cursor.Current)
                yield return document;
    }

    /// <summary>
    /// The <c>$limit</c> of two that <c>AggregateSingleAsync</c> appends, built once per output type.
    /// </summary>
    /// <remarks>
    /// The stage is a BSON document the driver renders as it stands, with the serializer of the call that renders it,
    /// so one instance serves every call; building it per call cost a document, its element list and the value.
    /// </remarks>
    /// <typeparam name="T">The type of the documents the pipeline yields.</typeparam>
    private static class Pair<T>
    {
        /// <summary>
        /// The stage.
        /// </summary>
        public static readonly PipelineStageDefinition<T, T> Limit = PipelineStageDefinitionBuilder.Limit<T>(2);
    }

    /// <summary>
    /// A collection kept for a document type and the convention that named it.
    /// </summary>
    /// <param name="Naming">The convention that named the collection.</param>
    /// <param name="Collection">The driver's collection object.</param>
    private sealed record Conventional(Func<Type, string> Naming, object Collection);
}
