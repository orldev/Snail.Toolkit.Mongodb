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

        return services.Register(
            serviceKey: null, sectionName, options => BindSection(options, configuration, sectionName), setup);
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

        return services.Register(serviceKey: null, sectionName: null, builder => builder.Configure(options), setup);
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

        return services.Register(
            serviceKey, sectionName, options => BindSection(options, configuration, sectionName), setup);
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

        return services.Register(serviceKey, sectionName: null, builder => builder.Configure(options), setup);
    }

    private static void BindSection(
        OptionsBuilder<MongoOptions> options,
        IConfiguration configuration,
        string sectionName) =>
        options
            .Bind(configuration.GetSection(sectionName))
            .Validate(
                _ => configuration.GetSection(sectionName).Exists(),
                $"Configuration section '{sectionName}' was not found. Add it, or pass a different section name.");

    /// <summary>
    /// Registers the validator, the setup instance and the three driver types.
    /// </summary>
    /// <remarks>
    /// One code path serves both the keyed and the unkeyed case: a <see cref="ServiceDescriptor"/>
    /// built with a null key <em>is</em> an unkeyed descriptor, and the key travels into the factory,
    /// where it doubles as the options name. Construction of the client is deferred to the first
    /// resolve, so a registration skipped by <c>TryAdd</c> leaves no orphaned connection pool behind.
    /// Conventions are the exception: they have to reach the driver before any document type is
    /// serialised, so they are applied here rather than when the client is built.
    ///
    /// A repeated call for the same key keeps the first registration. <c>TryAdd</c> alone did not:
    /// options configuration is additive, so a second call bound its own section over the first
    /// registration's settings, and a second call naming a section that did not exist failed the
    /// whole host even though the first call was valid.
    ///
    /// Staying silent about it is only right where the two calls cannot be told apart. Settings
    /// arrive as delegates, which compare to nothing, so a repeated delegate registration is a plain
    /// no-op — that is what lets one package call this defensively without breaking an application
    /// that already called it. A section name is a value, so two calls naming two sections are a
    /// mistake the compiler cannot catch and this can: they want two databases out of an API that
    /// registers one, and the answer is <c>AddKeyedMongodb</c>.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="serviceKey">The key to register under, or null for the unkeyed registration.</param>
    /// <param name="sectionName">The configuration section the settings came from, if any.</param>
    /// <param name="configureOptions">Binds or configures the settings.</param>
    /// <param name="setup">Configures delegates and driver option objects.</param>
    /// <exception cref="InvalidOperationException">
    /// The key is already registered from a different configuration section.
    /// </exception>
    private static IServiceCollection Register(
        this IServiceCollection services,
        string? serviceKey,
        string? sectionName,
        Action<OptionsBuilder<MongoOptions>> configureOptions,
        Action<MongoSetup>? setup)
    {
        if (services.Registration(serviceKey) is { } first)
        {
            if (AreDifferent(first.SectionName, sectionName))
                throw new InvalidOperationException(
                    $"MongoDB is already registered{Key(serviceKey)} from configuration section " +
                    $"'{first.SectionName}', and this call names '{sectionName}'. One registration " +
                    "serves one database; use AddKeyedMongodb with a distinct key for a second one.");

            return services;
        }

        services.Add(ServiceDescriptor.KeyedSingleton(serviceKey, new Registered(sectionName)));

        var options = services.AddOptions<MongoOptions>(OptionsName(serviceKey));
        configureOptions(options);

        options
            .Validate(
                o => string.IsNullOrWhiteSpace(o.Connection) || CanParse(o.Connection),
                "Connection is not a connection string the MongoDB driver can parse.")
            .Validate(
                o => string.IsNullOrWhiteSpace(o.Database) || DatabaseNamespace.IsValid(o.Database),
                "Database is not a legal MongoDB database name: it must not contain '.' or a null character.")
            .ValidateOnStart();

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<MongoOptions>>(MongoOptionsValidator.Instance));

        if (setup is not null)
        {
            services.AddOptions<MongoSetup>(OptionsName(serviceKey)).Configure(setup);

            Mongo.ApplyConventions(Configured(setup));
        }

        services.TryAdd(ServiceDescriptor.KeyedSingleton<IMongoClient>(
            serviceKey,
            (sp, key) => Mongo.Client(sp.Options(key), sp.Setup(key))));

        services.TryAdd(ServiceDescriptor.KeyedSingleton<IMongoDatabase>(
            serviceKey,
            (sp, key) => sp.GetRequiredKeyedService<IMongoClient>(key).GetDatabase(sp.Options(key).Database)));

        services.TryAdd(ServiceDescriptor.KeyedSingleton<IGridFSBucket>(
            serviceKey,
            (sp, key) => Mongo.GridFs(
                sp.GetRequiredKeyedService<IMongoDatabase>(key), sp.Setup(key))));

        return services;
    }

    /// <summary>
    /// Records that a key has been registered, and what it was registered from.
    /// </summary>
    private sealed record Registered(string? SectionName);

    private static Registered? Registration(this IServiceCollection services, string? serviceKey) =>
        services.FirstOrDefault(d => d.ServiceType == typeof(Registered) && Equals(d.ServiceKey, serviceKey))
            is { } descriptor
            ? (descriptor.IsKeyedService
                ? descriptor.KeyedImplementationInstance
                : descriptor.ImplementationInstance) as Registered
            : null;

    /// <summary>
    /// Two section names that name two databases. A call configuring settings by delegate names none,
    /// and says nothing either way.
    /// </summary>
    private static bool AreDifferent(string? first, string? second) =>
        first is not null &&
        second is not null &&
        string.Equals(first, second, StringComparison.OrdinalIgnoreCase) is false;

    private static string Key(string? serviceKey) =>
        serviceKey is null ? string.Empty : $" under key '{serviceKey}'";

    /// <summary>
    /// Whether the driver can make a connection string of this.
    /// </summary>
    /// <remarks>
    /// <c>[Required]</c> only asks whether the string is there. A present but unparseable one passed
    /// validation, brought the host up, and surfaced from the first query instead — which is exactly
    /// what validating at start-up is supposed to prevent. The driver exposes no non-throwing parse,
    /// so this is the only way to ask.
    /// </remarks>
    private static bool CanParse(string connection)
    {
        try
        {
            MongoClientSettings.FromConnectionString(connection);

            return true;
        }
        catch (MongoConfigurationException)
        {
            return false;
        }
    }

    /// <summary>
    /// The settings as the caller configured them, needed before the container can build them.
    /// </summary>
    private static MongoSetup Configured(Action<MongoSetup> setup)
    {
        var value = new MongoSetup();
        setup(value);

        return value;
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
