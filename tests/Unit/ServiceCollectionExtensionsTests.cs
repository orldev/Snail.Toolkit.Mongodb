using Microsoft.Extensions.Configuration;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Conventions;
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
            .AddMongodb(Given.Configuration())
            .BuildServiceProvider();

    private static IHost Host(IConfiguration configuration) =>
        new HostBuilder()
            .ConfigureServices(services => services.AddMongodb(configuration))
            .Build();

    [Fact]
    public void AddMongo_RegistersTheDriverTypes()
    {
        var services = new ServiceCollection().AddMongodb(Given.Configuration());

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
        var services = new ServiceCollection().AddMongodb(Given.Configuration());

        var mongoTypes = services
            .Select(d => d.ServiceType)
            .Where(t => t.Namespace?.StartsWith("MongoDB", StringComparison.Ordinal) is true)
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
            Given.DatabaseName,
            provider.GetRequiredService<IMongoDatabase>().DatabaseNamespace.DatabaseName);
    }

    [Fact]
    public void AddMongo_ReadsACustomSectionName()
    {
        var configuration = Given.Configuration(new Dictionary<string, string>
        {
            { "storage:Connection", Given.Connection },
            { "storage:Database", "custom" }
        });

        using var provider = new ServiceCollection()
            .AddMongodb(configuration, "storage")
            .BuildServiceProvider();

        Assert.Equal(
            "custom",
            provider.GetRequiredService<IMongoDatabase>().DatabaseNamespace.DatabaseName);
    }

    /// <summary>
    /// A class map is frozen the first time its type is serialised, so conventions that arrive when
    /// the client is first resolved miss every type touched before that — and what gets touched first
    /// differs between a development run and a production one. Registration has to apply them.
    /// </summary>
    [Fact]
    public void AddMongodb_AppliesConventionsWithoutResolvingAnything()
    {
        var packName = $"test-{Guid.NewGuid():N}";

        try
        {
            new ServiceCollection().AddMongodb(
                o =>
                {
                    o.Connection = Given.Connection;
                    o.Database = Given.DatabaseName;
                },
                s =>
                {
                    s.ConventionPackName = packName;
                    s.Conventions = pack => pack.Add(new CamelCaseElementNameConvention());
                });

            Assert.Contains("\"userName\"", new ConventionProbe { UserName = "ann" }.ToJson(), StringComparison.Ordinal);
        }
        finally
        {
            ConventionRegistry.Remove(packName);
        }
    }

    [Fact]
    public void AddMongo_AppliesTheSetupDelegate()
    {
        using var provider = new ServiceCollection()
            .AddMongodb(Given.Configuration(), setup: s => s.GridFsBucket = new GridFSBucketOptions
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
        var configuration = Given.Configuration(new Dictionary<string, string>
        {
            { "other:Connection", Given.Connection }
        });

        using var host = Host(configuration);

        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());

        Assert.Contains(MongoServiceCollectionExtensions.DefaultSectionName, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddMongo_WhenConnectionIsMissing_FailsStartupNamingTheProperty()
    {
        var configuration = Given.Configuration(new Dictionary<string, string>
        {
            { $"{Given.Section}:Database", Given.DatabaseName }
        });

        using var host = Host(configuration);

        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());

        Assert.Contains(nameof(MongoOptions.Connection), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddMongo_WhenDatabaseIsMissing_FailsStartupNamingTheProperty()
    {
        var configuration = Given.Configuration(new Dictionary<string, string>
        {
            { $"{Given.Section}:Connection", Given.Connection }
        });

        using var host = Host(configuration);

        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());

        Assert.Contains(nameof(MongoOptions.Database), exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// [Required] only asks whether the string is there. A connection string the driver cannot parse
    /// is present and unusable, so the host came up and the first query — possibly minutes later,
    /// possibly in production — was the one to find out.
    /// </summary>
    [Fact]
    public async Task AddMongodb_WhenTheConnectionStringCannotBeParsed_FailsStartup()
    {
        var configuration = Given.Configuration(new Dictionary<string, string>
        {
            { $"{Given.Section}:Connection", "this is not a connection string" },
            { $"{Given.Section}:Database", Given.DatabaseName }
        });

        using var host = Host(configuration);

        await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());
    }

    /// <summary>
    /// The same hole one property along: MongoDB rejects a database name containing a dot, and the
    /// rejection arrived from GetDatabase at first resolve rather than from validation at start-up.
    /// </summary>
    [Fact]
    public async Task AddMongodb_WhenTheDatabaseNameIsIllegal_FailsStartup()
    {
        var configuration = Given.Configuration(new Dictionary<string, string>
        {
            { $"{Given.Section}:Connection", Given.Connection },
            { $"{Given.Section}:Database", "has.dots" }
        });

        using var host = Host(configuration);

        await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());
    }

    /// <summary>
    /// The three registrations are singletons resolved lazily, so the first burst of traffic after a
    /// cold start is where a broken factory would show up.
    /// </summary>
    [Fact]
    public void Resolving_FromManyThreadsAtOnce_YieldsOneClient()
    {
        using var provider = Provider();

        var clients = new System.Collections.Concurrent.ConcurrentBag<IMongoClient>();
        var databases = new System.Collections.Concurrent.ConcurrentBag<IMongoDatabase>();

        Parallel.For(0, 64, _ =>
        {
            clients.Add(provider.GetRequiredService<IMongoClient>());
            databases.Add(provider.GetRequiredService<IMongoDatabase>());
        });

        Assert.Single(clients.Distinct());
        Assert.Single(databases.Distinct());
    }

    [Fact]
    public async Task AddMongo_WithCompleteSettings_StartsUp()
    {
        using var host = Host(Given.Configuration());

        await host.StartAsync();
        await host.StopAsync();
    }

    [Fact]
    public void AddMongo_WhenSettingsAreIncomplete_AlsoFailsOnResolve()
    {
        var configuration = Given.Configuration(new Dictionary<string, string>
        {
            { $"{Given.Section}:Database", Given.DatabaseName }
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
        var services = new ServiceCollection().AddMongodb(Given.Configuration());
        services.Configure<MongoOptions>(o => o.Database = "overridden");

        using var provider = services.BuildServiceProvider();

        Assert.Equal(
            "overridden",
            provider.GetRequiredService<IMongoDatabase>().DatabaseNamespace.DatabaseName);
    }

    [Fact]
    public void PostConfigure_AfterAddMongo_OverridesTheBoundSettings()
    {
        var services = new ServiceCollection().AddMongodb(Given.Configuration());
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

        Assert.Equal(Given.Connection, options.Connection);
        Assert.Equal(Given.DatabaseName, options.Database);
    }

    [Fact]
    public void AddMongo_WithDelegate_Registers()
    {
        using var provider = new ServiceCollection()
            .AddMongodb(o =>
            {
                o.Connection = Given.Connection;
                o.Database = Given.DatabaseName;
            })
            .BuildServiceProvider();

        Assert.NotNull(provider.GetService<IMongoDatabase>());
    }

    [Fact]
    public void AddMongo_WithNullArguments_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentNullException>(() => ((IServiceCollection)null!).AddMongodb(Given.Configuration()));
        Assert.Throws<ArgumentNullException>(() => services.AddMongodb((IConfiguration)null!));
        Assert.Throws<ArgumentNullException>(() => services.AddMongodb((Action<MongoOptions>)null!));
        Assert.Throws<ArgumentNullException>(() => services.AddKeyedMongodb(null!, Given.Configuration()));
    }

    /// <summary>
    /// The defensive case: one package calls <c>AddMongodb</c> on an application that already did.
    /// The two calls are indistinguishable, so the first registration simply stands.
    /// </summary>
    [Fact]
    public void AddMongodb_WhenCalledTwiceForTheSameSection_KeepsTheFirstRegistration()
    {
        var configuration = Given.Configuration();

        var services = new ServiceCollection()
            .AddMongodb(configuration)
            .AddMongodb(configuration, setup: s => s.GridFsBucket = new GridFSBucketOptions
            {
                BucketName = "ignored"
            });

        using var provider = services.BuildServiceProvider();

        Assert.Single(services, d => d.ServiceType == typeof(IMongoClient));
        Assert.Equal(Given.DatabaseName, provider.GetRequiredService<IMongoDatabase>().DatabaseNamespace.DatabaseName);
        Assert.Equal("fs", provider.GetRequiredService<IGridFSBucket>().Options.BucketName);
    }

    /// <summary>
    /// Settings given as a delegate compare to nothing, so a repeated call cannot be judged and the
    /// first registration stands rather than the second silently binding over it.
    /// </summary>
    [Fact]
    public void AddMongodb_WhenCalledTwiceWithDelegates_KeepsTheFirstRegistration()
    {
        using var provider = new ServiceCollection()
            .AddMongodb(o =>
            {
                o.Connection = Given.Connection;
                o.Database = "first";
            })
            .AddMongodb(o =>
            {
                o.Connection = Given.Connection;
                o.Database = "second";
            })
            .BuildServiceProvider();

        Assert.Equal("first", provider.GetRequiredService<IMongoDatabase>().DatabaseNamespace.DatabaseName);
    }

    /// <summary>
    /// Two section names are two databases asked of an API that registers one. Binding was additive,
    /// so this used to leave one client silently pointed at the second section; it is a mistake worth
    /// naming, and the fix is a keyed registration.
    /// </summary>
    [Fact]
    public void AddMongodb_WhenASecondCallNamesAnotherSection_ThrowsNamingBoth()
    {
        var configuration = Given.Configuration(new Dictionary<string, string>
        {
            { "first:Connection", Given.Connection },
            { "first:Database", "first" },
            { "second:Connection", Given.Connection },
            { "second:Database", "second" }
        });

        var services = new ServiceCollection().AddMongodb(configuration, "first");

        var exception = Assert.Throws<InvalidOperationException>(
            () => services.AddMongodb(configuration, "second"));

        Assert.Contains("first", exception.Message, StringComparison.Ordinal);
        Assert.Contains("second", exception.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(MongoServiceCollectionExtensions.AddKeyedMongodb), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddKeyedMongodb_WhenASecondCallNamesAnotherSection_ThrowsNamingTheKey()
    {
        var configuration = Given.Configuration(new Dictionary<string, string>
        {
            { "first:Connection", Given.Connection },
            { "first:Database", "first" },
            { "second:Connection", Given.Connection },
            { "second:Database", "second" }
        });

        var services = new ServiceCollection().AddKeyedMongodb("orders", configuration, "first");

        var exception = Assert.Throws<InvalidOperationException>(
            () => services.AddKeyedMongodb("orders", configuration, "second"));

        Assert.Contains("orders", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A second call naming a section that does not exist used to fail the whole host through
    /// <c>ValidateOnStart</c>, quoting a section the first caller never asked for. It is now refused
    /// where the mistake is, at registration.
    /// </summary>
    [Fact]
    public void AddMongodb_WhenASecondCallNamesAMissingSection_ThrowsAtRegistration()
    {
        var configuration = Given.Configuration();

        var services = new ServiceCollection().AddMongodb(configuration);

        Assert.Throws<InvalidOperationException>(() => services.AddMongodb(configuration, "does-not-exist"));
    }

    /// <summary>
    /// The whole point of the defensive case: two identical registrations must still bring the host up.
    /// </summary>
    [Fact]
    public async Task AddMongodb_WhenCalledTwiceForTheSameSection_StartsUp()
    {
        var configuration = Given.Configuration();

        using var host = new HostBuilder()
            .ConfigureServices(services => services
                .AddMongodb(configuration)
                .AddMongodb(configuration))
            .Build();

        await host.StartAsync();
        await host.StopAsync();
    }

    /// <summary>
    /// Registration must not build a client. Were the instance created as an argument to
    /// <c>TryAddSingleton</c>, a skipped registration would still spin up a connection pool.
    /// </summary>
    [Fact]
    public void AddMongo_WhenAClientIsAlreadyRegistered_DoesNotBuildASecondOne()
    {
        var existing = new MongoClient(Given.Connection);

        using var provider = new ServiceCollection()
            .AddSingleton<IMongoClient>(existing)
            .AddMongodb(o =>
            {
                o.Connection = "mongodb://this-would-throw-if-eagerly-parsed:not-a-port";
                o.Database = Given.DatabaseName;
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
                o.Connection = Given.Connection;
                o.Database = "orders";
            })
            .AddKeyedMongodb("audit", o =>
            {
                o.Connection = Given.Connection;
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
            .AddKeyedMongodb("orders", Given.Configuration())
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
            .AddKeyedMongodb("orders", Given.Configuration())
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
                o.Connection = Given.Connection;
                o.Database = "orders";
            })
            .AddKeyedMongodb("audit", o =>
            {
                o.Connection = Given.Connection;
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

    private sealed class ConventionProbe
    {
        public string? UserName { get; set; }
    }
}
