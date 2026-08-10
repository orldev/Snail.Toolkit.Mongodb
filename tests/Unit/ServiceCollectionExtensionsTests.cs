using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using MongoDB.Driver.GridFS;
using Snail.Toolkit.Mongodb.DependencyInjection;

namespace Snail.Toolkit.Mongodb.Tests.Unit;

/// <summary>
/// What goes into the container is the driver's own types, so a class can ask for just the one it
/// needs — a repository that only reads takes <see cref="IMongoDatabase"/> and never sees a client.
/// </summary>
public class ServiceCollectionExtensionsTests
{
    private static ServiceProvider Provider() =>
        new ServiceCollection()
            .AddMongodb(Helper.GetConfiguration())
            .BuildServiceProvider();

    private static IHost Host(IConfiguration configuration) =>
        new HostBuilder()
            .ConfigureServices(services => services.AddMongodb(configuration))
            .Build();

    [Fact]
    public void AddMongo_RegistersTheDriverTypes()
    {
        var services = new ServiceCollection().AddMongodb(Helper.GetConfiguration());

        Assert.Contains(services, d => d.ServiceType == typeof(IMongoClient));
        Assert.Contains(services, d => d.ServiceType == typeof(IMongoDatabase));
        Assert.Contains(services, d => d.ServiceType == typeof(IGridFSBucket));
    }

    /// <summary>
    /// No wrapper type is registered — that is the point of dropping it.
    /// </summary>
    [Fact]
    public void AddMongo_RegistersNothingBesidesDriverTypesAndOptions()
    {
        var services = new ServiceCollection().AddMongodb(Helper.GetConfiguration());

        var mongoTypes = services
            .Select(d => d.ServiceType)
            .Where(t => t.Namespace?.StartsWith("MongoDB") is true)
            .Distinct()
            .Order(Comparer<Type>.Create((a, b) => string.CompareOrdinal(a.Name, b.Name)))
            .Select(t => t.Name);

        Assert.Equal(["IGridFSBucket", "IMongoClient", "IMongoDatabase"], mongoTypes);
    }

    [Fact]
    public void AddMongo_ResolvesAllThree()
    {
        using var provider = Provider();

        Assert.NotNull(provider.GetService<IMongoClient>());
        Assert.NotNull(provider.GetService<IMongoDatabase>());
        Assert.NotNull(provider.GetService<IGridFSBucket>());
    }

    /// <summary>
    /// The database must come off the registered client, not a second one — otherwise the
    /// application would quietly hold two connection pools.
    /// </summary>
    [Fact]
    public void AddMongo_DatabaseComesOffTheRegisteredClient()
    {
        using var provider = Provider();

        Assert.Same(
            provider.GetRequiredService<IMongoClient>(),
            provider.GetRequiredService<IMongoDatabase>().Client);
    }

    [Fact]
    public void AddMongo_ResolvesSingletons()
    {
        using var provider = Provider();

        Assert.Same(provider.GetRequiredService<IMongoClient>(), provider.GetRequiredService<IMongoClient>());
        Assert.Same(provider.GetRequiredService<IMongoDatabase>(), provider.GetRequiredService<IMongoDatabase>());
        Assert.Same(provider.GetRequiredService<IGridFSBucket>(), provider.GetRequiredService<IGridFSBucket>());
    }

    [Fact]
    public void AddMongo_UsesTheConfiguredDatabase()
    {
        using var provider = Provider();

        Assert.Equal(
            Helper.Database,
            provider.GetRequiredService<IMongoDatabase>().DatabaseNamespace.DatabaseName);
    }

    [Fact]
    public void AddMongo_ReadsACustomSectionName()
    {
        var configuration = Helper.GetConfiguration(new Dictionary<string, string>
        {
            { "storage:Connection", Helper.Connection },
            { "storage:Database", "custom" }
        });

        using var provider = new ServiceCollection()
            .AddMongodb(configuration, "storage")
            .BuildServiceProvider();

        Assert.Equal(
            "custom",
            provider.GetRequiredService<IMongoDatabase>().DatabaseNamespace.DatabaseName);
    }

    [Fact]
    public void AddMongo_AppliesTheSetupDelegate()
    {
        using var provider = new ServiceCollection()
            .AddMongodb(Helper.GetConfiguration(), setup: s => s.GridFsBucket = new GridFSBucketOptions
            {
                BucketName = "attachments"
            })
            .BuildServiceProvider();

        Assert.Equal("attachments", provider.GetRequiredService<IGridFSBucket>().Options.BucketName);
    }

    /// <summary>
    /// Validation is deferred to startup rather than run inside <c>AddMongo</c>, which is what
    /// <c>ValidateOnStart</c> buys: the process refuses to come up instead of failing on first query.
    /// </summary>
    [Fact]
    public async Task AddMongo_WhenSectionIsMissing_FailsStartupWithTheSectionName()
    {
        var configuration = Helper.GetConfiguration(new Dictionary<string, string>
        {
            { "other:Connection", Helper.Connection }
        });

        using var host = Host(configuration);

        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());

        Assert.Contains(MongoServiceCollectionExtensions.DefaultSectionName, exception.Message);
    }

    [Fact]
    public async Task AddMongo_WhenConnectionIsMissing_FailsStartupNamingTheProperty()
    {
        var configuration = Helper.GetConfiguration(new Dictionary<string, string>
        {
            { $"{Helper.Section}:Database", Helper.Database }
        });

        using var host = Host(configuration);

        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());

        Assert.Contains(nameof(MongoOptions.Connection), exception.Message);
    }

    [Fact]
    public async Task AddMongo_WhenDatabaseIsMissing_FailsStartupNamingTheProperty()
    {
        var configuration = Helper.GetConfiguration(new Dictionary<string, string>
        {
            { $"{Helper.Section}:Connection", Helper.Connection }
        });

        using var host = Host(configuration);

        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());

        Assert.Contains(nameof(MongoOptions.Database), exception.Message);
    }

    [Fact]
    public async Task AddMongo_WithCompleteSettings_StartsUp()
    {
        using var host = Host(Helper.GetConfiguration());

        await host.StartAsync();
        await host.StopAsync();
    }

    [Fact]
    public void AddMongo_WhenSettingsAreIncomplete_AlsoFailsOnResolve()
    {
        var configuration = Helper.GetConfiguration(new Dictionary<string, string>
        {
            { $"{Helper.Section}:Database", Helper.Database }
        });

        using var provider = new ServiceCollection()
            .AddMongodb(configuration)
            .BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IMongoClient>());
    }

    /// <summary>
    /// The point of going through the options pattern: settings stay adjustable after registration.
    /// </summary>
    [Fact]
    public void Configure_AfterAddMongo_OverridesTheBoundSettings()
    {
        var services = new ServiceCollection().AddMongodb(Helper.GetConfiguration());
        services.Configure<MongoOptions>(o => o.Database = "overridden");

        using var provider = services.BuildServiceProvider();

        Assert.Equal(
            "overridden",
            provider.GetRequiredService<IMongoDatabase>().DatabaseNamespace.DatabaseName);
    }

    [Fact]
    public void PostConfigure_AfterAddMongo_OverridesTheBoundSettings()
    {
        var services = new ServiceCollection().AddMongodb(Helper.GetConfiguration());
        services.PostConfigure<MongoOptions>(o => o.Database = "post");

        using var provider = services.BuildServiceProvider();

        Assert.Equal(
            "post",
            provider.GetRequiredService<IMongoDatabase>().DatabaseNamespace.DatabaseName);
    }

    [Fact]
    public void AddMongo_ExposesTheOptions()
    {
        using var provider = Provider();

        var options = provider.GetRequiredService<IOptions<MongoOptions>>().Value;

        Assert.Equal(Helper.Connection, options.Connection);
        Assert.Equal(Helper.Database, options.Database);
    }

    [Fact]
    public void AddMongo_WithDelegate_Registers()
    {
        using var provider = new ServiceCollection()
            .AddMongodb(o =>
            {
                o.Connection = Helper.Connection;
                o.Database = Helper.Database;
            })
            .BuildServiceProvider();

        Assert.NotNull(provider.GetService<IMongoDatabase>());
    }

    [Fact]
    public void AddMongo_WithNullArguments_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentNullException>(() => ((IServiceCollection)null!).AddMongodb(Helper.GetConfiguration()));
        Assert.Throws<ArgumentNullException>(() => services.AddMongodb((IConfiguration)null!));
        Assert.Throws<ArgumentNullException>(() => services.AddMongodb((Action<MongoOptions>)null!));
        Assert.Throws<ArgumentNullException>(() => services.AddKeyedMongodb(null!, Helper.GetConfiguration()));
    }

    [Fact]
    public void AddMongo_IsIdempotent()
    {
        var configuration = Helper.GetConfiguration();

        var services = new ServiceCollection()
            .AddMongodb(configuration)
            .AddMongodb(configuration);

        Assert.Single(services, d => d.ServiceType == typeof(IMongoClient));
    }

    /// <summary>
    /// Registration must not build a client. Were the instance created as an argument to
    /// <c>TryAddSingleton</c>, a skipped registration would still spin up a connection pool.
    /// </summary>
    [Fact]
    public void AddMongo_WhenAClientIsAlreadyRegistered_DoesNotBuildASecondOne()
    {
        var existing = new MongoClient(Helper.Connection);

        using var provider = new ServiceCollection()
            .AddSingleton<IMongoClient>(existing)
            .AddMongodb(o =>
            {
                o.Connection = "mongodb://this-would-throw-if-eagerly-parsed:not-a-port";
                o.Database = Helper.Database;
            })
            .BuildServiceProvider();

        Assert.Same(existing, provider.GetRequiredService<IMongoClient>());
    }

    /// <summary>
    /// Two databases in one application: the unkeyed registration cannot express this, because
    /// <c>TryAdd</c> makes the second call a no-op.
    /// </summary>
    [Fact]
    public void AddKeyedMongo_RegistersOneSetPerKey()
    {
        using var provider = new ServiceCollection()
            .AddKeyedMongodb("orders", o =>
            {
                o.Connection = Helper.Connection;
                o.Database = "orders";
            })
            .AddKeyedMongodb("audit", o =>
            {
                o.Connection = Helper.Connection;
                o.Database = "audit";
            })
            .BuildServiceProvider();

        var orders = provider.GetRequiredKeyedService<IMongoDatabase>("orders");
        var audit = provider.GetRequiredKeyedService<IMongoDatabase>("audit");

        Assert.NotSame(orders, audit);
        Assert.NotSame(orders.Client, audit.Client);
        Assert.Equal("orders", orders.DatabaseNamespace.DatabaseName);
        Assert.Equal("audit", audit.DatabaseNamespace.DatabaseName);
    }

    [Fact]
    public void AddKeyedMongo_KeepsTheThreeTypesConsistentUnderAKey()
    {
        using var provider = new ServiceCollection()
            .AddKeyedMongodb("orders", Helper.GetConfiguration())
            .BuildServiceProvider();

        var client = provider.GetRequiredKeyedService<IMongoClient>("orders");
        var database = provider.GetRequiredKeyedService<IMongoDatabase>("orders");
        var bucket = provider.GetRequiredKeyedService<IGridFSBucket>("orders");

        Assert.Same(client, database.Client);
        Assert.Same(database, bucket.Database);
    }

    [Fact]
    public void AddKeyedMongo_DoesNotRegisterTheUnkeyedTypes()
    {
        using var provider = new ServiceCollection()
            .AddKeyedMongodb("orders", Helper.GetConfiguration())
            .BuildServiceProvider();

        Assert.Null(provider.GetService<IMongoClient>());
        Assert.Null(provider.GetService<IMongoDatabase>());
    }

    /// <summary>
    /// The key doubles as the options name, so named configuration reaches exactly one database.
    /// </summary>
    [Fact]
    public void Configure_WithAKey_OverridesOnlyThatDatabase()
    {
        var services = new ServiceCollection()
            .AddKeyedMongodb("orders", o =>
            {
                o.Connection = Helper.Connection;
                o.Database = "orders";
            })
            .AddKeyedMongodb("audit", o =>
            {
                o.Connection = Helper.Connection;
                o.Database = "audit";
            });

        services.Configure<MongoOptions>("orders", o => o.Database = "orders-v2");

        using var provider = services.BuildServiceProvider();

        Assert.Equal(
            "orders-v2",
            provider.GetRequiredKeyedService<IMongoDatabase>("orders").DatabaseNamespace.DatabaseName);
        Assert.Equal(
            "audit",
            provider.GetRequiredKeyedService<IMongoDatabase>("audit").DatabaseNamespace.DatabaseName);
    }
}
