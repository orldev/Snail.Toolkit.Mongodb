using Microsoft.Extensions.Options;

namespace Snail.Toolkit.Mongodb;

/// <summary>
/// The driver's types, built from settings.
/// </summary>
/// <remarks>
/// Used by the DI registration, and usable directly in a console app, a migration or a test where
/// there is no container.
/// </remarks>
public static class Mongo
{
    private static readonly Lock ConventionGate = new();
    private static readonly HashSet<string> AppliedPacks = new(StringComparer.Ordinal);

    /// <summary>
    /// A client for the given settings.
    /// </summary>
    /// <remarks>
    /// The caller owns the returned client and its connection pool, and should keep one per
    /// application and dispose it at shutdown. Under DI the container does both.
    /// </remarks>
    /// <param name="options">The settings.</param>
    /// <param name="setup">Delegates and driver option objects that cannot come from configuration.</param>
    /// <returns>A client.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
    /// <exception cref="OptionsValidationException">The settings are incomplete.</exception>
    public static IMongoClient Client(MongoOptions options, MongoSetup? setup = null)
    {
        MongoOptionsValidator.ThrowIfInvalid(options, nameof(MongoOptions));

        var config = setup ?? new MongoSetup();
        ApplyConventions(config);

        var settings = MongoClientSettings.FromConnectionString(options.Connection);
        config.ConfigureClient?.Invoke(settings);

        return new MongoClient(settings);
    }

    /// <summary>
    /// The configured database, off a client built for the given settings.
    /// </summary>
    /// <remarks>
    /// The client behind the database is owned by the caller through
    /// <see cref="IMongoDatabase.Client"/>; dispose it at shutdown.
    /// </remarks>
    /// <param name="options">The settings.</param>
    /// <param name="setup">Delegates and driver option objects that cannot come from configuration.</param>
    /// <returns>A database.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
    /// <exception cref="OptionsValidationException">The settings are incomplete.</exception>
    public static IMongoDatabase Database(MongoOptions options, MongoSetup? setup = null) =>
        Client(options, setup).GetDatabase(options.Database);

    /// <summary>
    /// A GridFS bucket over the given database.
    /// </summary>
    /// <param name="database">The database.</param>
    /// <param name="setup">Supplies the bucket options, if any.</param>
    /// <returns>A bucket.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="database"/> is null.</exception>
    public static IGridFSBucket GridFs(IMongoDatabase database, MongoSetup? setup = null)
    {
        ArgumentNullException.ThrowIfNull(database);

        return new GridFSBucket(database, setup?.GridFsBucket);
    }

    /// <summary>
    /// Registers the configured conventions in the driver's process-global registry, once per name.
    /// </summary>
    /// <remarks>
    /// A convention only reaches a type whose class map has not been built yet, and a class map stays
    /// frozen for the life of the process. Applying conventions when the client is first resolved was
    /// therefore a race against whatever serialised first: a type touched by a warm-up or a background
    /// job kept its unconverted element names, so the same code wrote <c>UserName</c> in one process
    /// and <c>userName</c> in the next. The registration path calls this while the container is still
    /// being built, before anything can serialise.
    ///
    /// The registry has no upsert, and removing the pack before re-adding it left a window in which a
    /// concurrent class map was built without the pack and stayed that way. Registering once per name
    /// and skipping afterwards closes that window; the first registration of a name wins.
    /// </remarks>
    /// <param name="setup">Supplies the conventions and the name to register them under.</param>
    /// <exception cref="ArgumentNullException"><paramref name="setup"/> is null.</exception>
    public static void ApplyConventions(MongoSetup setup)
    {
        ArgumentNullException.ThrowIfNull(setup);

        if (setup.Conventions is null) return;

        ArgumentException.ThrowIfNullOrWhiteSpace(setup.ConventionPackName);

        var pack = new ConventionPack();
        setup.Conventions.Invoke(pack);

        if (pack.Conventions.Any() is false) return;

        lock (ConventionGate)
        {
            if (AppliedPacks.Add(setup.ConventionPackName) is false) return;

            ConventionRegistry.Register(setup.ConventionPackName, pack, _ => true);
        }
    }
}
