using Microsoft.Extensions.Options;

namespace Snail.Toolkit.Mongodb.Services;

/// <summary>
/// Builds the driver's types from settings. Used by the DI registration, and usable directly in a
/// console app or a test where there is no container.
/// </summary>
public static class MongoFactory
{
    private static readonly Lock ConventionGate = new();

    /// <summary>
    /// Creates a client from the given settings.
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
    public static IMongoClient CreateClient(MongoOptions options, MongoSetup? setup = null)
    {
        MongoOptionsValidator.ThrowIfInvalid(options, nameof(MongoOptions));

        var config = setup ?? new MongoSetup();
        ApplyConventions(config);

        var settings = MongoClientSettings.FromConnectionString(options.Connection);
        config.ConfigureClient?.Invoke(settings);

        return new MongoClient(settings);
    }

    /// <summary>
    /// Creates a client and returns the configured database off it.
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
    public static IMongoDatabase CreateDatabase(MongoOptions options, MongoSetup? setup = null) =>
        CreateClient(options, setup).GetDatabase(options.Database);

    /// <summary>
    /// Creates a GridFS bucket over the given database.
    /// </summary>
    /// <param name="database">The database.</param>
    /// <param name="setup">Supplies the bucket options, if any.</param>
    /// <returns>A bucket.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="database"/> is null.</exception>
    public static IGridFSBucket CreateGridFsBucket(IMongoDatabase database, MongoSetup? setup = null)
    {
        ArgumentNullException.ThrowIfNull(database);

        return new GridFSBucket(database, setup?.GridFsBucket);
    }

    /// <summary>
    /// Registers the configured conventions. The driver's registry is process-global and has no
    /// upsert, so the pack is removed first to keep repeated creation idempotent.
    /// </summary>
    private static void ApplyConventions(MongoSetup setup)
    {
        if (setup.Conventions is null) return;

        ArgumentException.ThrowIfNullOrWhiteSpace(setup.ConventionPackName);

        var pack = new ConventionPack();
        setup.Conventions.Invoke(pack);

        if (pack.Conventions.Any() is false) return;

        lock (ConventionGate)
        {
            ConventionRegistry.Remove(setup.ConventionPackName);
            ConventionRegistry.Register(setup.ConventionPackName, pack, _ => true);
        }
    }
}
