using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Snail.Toolkit.Mongodb.DependencyInjection;

/// <summary>
/// Registers the MongoDB driver's own types from configuration.
/// </summary>
/// <remarks>
/// There is no wrapper interface: what goes into the container is <see cref="IMongoClient"/>,
/// <see cref="IMongoDatabase"/> and <see cref="IGridFSBucket"/>, so a class asks for exactly the one
/// it uses. The helpers in <c>Snail.Toolkit.Mongodb.Extensions</c> are extension methods on those
/// same types, which is why nothing has to be carried around to reach them.
/// </remarks>
public static class MongoServiceCollectionExtensions
{
    /// <summary>
    /// The configuration section read when none is named.
    /// </summary>
    public const string DefaultSectionName = "mongodb";

    /// <summary>
    /// Registers the MongoDB driver's types, binding <see cref="MongoOptions"/> from configuration.
    /// </summary>
    /// <remarks>
    /// Settings go through the standard options pattern, so <see cref="IOptions{TOptions}"/> of
    /// <see cref="MongoOptions"/> is resolvable and a consumer can still adjust them with
    /// <c>services.Configure&lt;MongoOptions&gt;(...)</c> or <c>PostConfigure</c>. They are validated
    /// when the host starts, so a missing setting refuses to bring the process up instead of
    /// surfacing on the first query. The client is built on first resolve and disposed by the
    /// container.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration to bind from.</param>
    /// <param name="sectionName">The configuration section holding the settings.</param>
    /// <param name="setup">Configures delegates and driver option objects that configuration cannot carry.</param>
    /// <returns>The service collection, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="configuration"/> is null.</exception>
    public static IServiceCollection AddMongodb(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName = DefaultSectionName,
        Action<MongoSetup>? setup = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionName);

        services.Bind(OptionsName(null), configuration, sectionName);

        return services.Register(serviceKey: null, setup);
    }

    /// <summary>
    /// Registers the MongoDB driver's types, configuring <see cref="MongoOptions"/> with a delegate.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="options">Configures the settings.</param>
    /// <param name="setup">Configures delegates and driver option objects that configuration cannot carry.</param>
    /// <returns>The service collection, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="options"/> is null.</exception>
    public static IServiceCollection AddMongodb(
        this IServiceCollection services,
        Action<MongoOptions> options,
        Action<MongoSetup>? setup = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        services.AddOptions<MongoOptions>()
            .Configure(options)
            .ValidateOnStart();

        return services.Register(serviceKey: null, setup);
    }

    /// <summary>
    /// Registers the MongoDB driver's types under a key, so that one application can talk to several
    /// databases side by side.
    /// </summary>
    /// <remarks>
    /// The key doubles as the name of the options instance, so
    /// <c>services.Configure&lt;MongoOptions&gt;("orders", ...)</c> adjusts exactly that database.
    /// Resolve with <c>[FromKeyedServices("orders")] IMongoDatabase</c>. Each key gets its own client,
    /// and so its own connection pool.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="serviceKey">The key, which is also the options name.</param>
    /// <param name="configuration">The configuration to bind from.</param>
    /// <param name="sectionName">The configuration section holding the settings.</param>
    /// <param name="setup">Configures delegates and driver option objects that configuration cannot carry.</param>
    /// <returns>The service collection, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/>, <paramref name="serviceKey"/> or <paramref name="configuration"/> is null.</exception>
    public static IServiceCollection AddKeyedMongodb(
        this IServiceCollection services,
        string serviceKey,
        IConfiguration configuration,
        string sectionName = DefaultSectionName,
        Action<MongoSetup>? setup = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceKey);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionName);

        services.Bind(serviceKey, configuration, sectionName);

        return services.Register(serviceKey, setup);
    }

    /// <summary>
    /// Registers the MongoDB driver's types under a key, so that one application can talk to several
    /// databases side by side.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="serviceKey">The key, which is also the options name.</param>
    /// <param name="options">Configures the settings.</param>
    /// <param name="setup">Configures delegates and driver option objects that configuration cannot carry.</param>
    /// <returns>The service collection, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/>, <paramref name="serviceKey"/> or <paramref name="options"/> is null.</exception>
    public static IServiceCollection AddKeyedMongodb(
        this IServiceCollection services,
        string serviceKey,
        Action<MongoOptions> options,
        Action<MongoSetup>? setup = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceKey);
        ArgumentNullException.ThrowIfNull(options);

        services.AddOptions<MongoOptions>(serviceKey)
            .Configure(options)
            .ValidateOnStart();

        return services.Register(serviceKey, setup);
    }

    private static void Bind(
        this IServiceCollection services,
        string optionsName,
        IConfiguration configuration,
        string sectionName) =>
        services.AddOptions<MongoOptions>(optionsName)
            .Bind(configuration.GetSection(sectionName))
            .Validate(
                _ => configuration.GetSection(sectionName).Exists(),
                $"Configuration section '{sectionName}' was not found. Add it, or pass a different section name.")
            .ValidateOnStart();

    /// <summary>
    /// Registers the validator, the setup instance and the three driver types.
    /// </summary>
    /// <remarks>
    /// One code path serves both the keyed and the unkeyed case: a <see cref="ServiceDescriptor"/>
    /// built with a null key <em>is</em> an unkeyed descriptor, and the key travels into the factory,
    /// where it doubles as the options name. Construction of the client is deferred to the first
    /// resolve, so a registration skipped by <c>TryAdd</c> leaves no orphaned connection pool behind.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="serviceKey">The key to register under, or null for the unkeyed registration.</param>
    /// <param name="setup">Configures delegates and driver option objects.</param>
    private static IServiceCollection Register(
        this IServiceCollection services,
        string? serviceKey,
        Action<MongoSetup>? setup)
    {
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<MongoOptions>>(MongoOptionsValidator.Instance));

        if (setup is not null)
            services.AddOptions<MongoSetup>(OptionsName(serviceKey)).Configure(setup);

        services.TryAdd(ServiceDescriptor.KeyedSingleton<IMongoClient>(
            serviceKey,
            (sp, key) => MongoFactory.CreateClient(sp.Options(key), sp.Setup(key))));

        services.TryAdd(ServiceDescriptor.KeyedSingleton<IMongoDatabase>(
            serviceKey,
            (sp, key) => sp.GetRequiredKeyedService<IMongoClient>(key).GetDatabase(sp.Options(key).Database)));

        services.TryAdd(ServiceDescriptor.KeyedSingleton<IGridFSBucket>(
            serviceKey,
            (sp, key) => MongoFactory.CreateGridFsBucket(
                sp.GetRequiredKeyedService<IMongoDatabase>(key), sp.Setup(key))));

        return services;
    }

    /// <summary>
    /// The service key doubles as the options name, so the two can never drift apart.
    /// </summary>
    private static string OptionsName(object? serviceKey) =>
        serviceKey as string ?? Microsoft.Extensions.Options.Options.DefaultName;

    private static MongoOptions Options(this IServiceProvider provider, object? serviceKey) =>
        provider.GetRequiredService<IOptionsMonitor<MongoOptions>>().Get(OptionsName(serviceKey));

    private static MongoSetup Setup(this IServiceProvider provider, object? serviceKey) =>
        provider.GetRequiredService<IOptionsMonitor<MongoSetup>>().Get(OptionsName(serviceKey));
}
