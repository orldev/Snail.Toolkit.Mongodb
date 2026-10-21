using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Conventions;
using Snail.Toolkit.Mongodb.DependencyInjection;

namespace Snail.Toolkit.Mongodb.Tests.Unit;

/// <summary>
/// Settings that are present and unusable, which start-up validation promises to refuse, and configuration that
/// used to be dropped without a word.
/// </summary>
public class RegistrationChaosTests
{
    private const string SixtyFourCharacters = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private static IHost Host(string connection, string database) =>
        new HostBuilder()
            .ConfigureServices(services => services.AddMongodb(o =>
            {
                o.Connection = connection;
                o.Database = database;
            }))
            .Build();

    /// <summary>
    /// The driver refuses a dot and a null character in a database name; the server also refuses a slash, a
    /// backslash, a quote, a dollar sign and a space, and anything of 64 characters or more — measured against
    /// mongo:8.0 in <c>Integration.ServerRefusalChaosTests</c>. These used to come up and fail on the first query.
    /// </summary>
    [Theory]
    [InlineData("orders/2026")]
    [InlineData("a$b")]
    [InlineData("back\\slash")]
    [InlineData("with\"quote")]
    [InlineData("with space")]
    [InlineData(SixtyFourCharacters)]
    public async Task AddMongodb_WithADatabaseNameTheServerRefuses_FailsStartup(string database)
    {
        using var host = Host(Given.Connection, database);

        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());

        Assert.Contains(nameof(MongoOptions.Database), exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The driver parses more than it can run. A value it refuses with another exception than
    /// <c>MongoConfigurationException</c> — <c>w=-1</c>, an unknown <c>authMechanism</c> — used to escape start-up
    /// as that exception, and <c>connectTimeoutMS=-5</c> parsed, brought the host up and threw from the client's
    /// constructor on first resolve. Every one is now a validation failure naming the setting.
    /// </summary>
    [Theory]
    [InlineData("mongodb://localhost:27017,localhost:27018/?directConnection=true")]
    [InlineData("mongodb://localhost:27017,localhost:27018/?loadBalanced=true")]
    [InlineData("mongodb://localhost:27017/?loadBalanced=true&replicaSet=rs0")]
    [InlineData("mongodb://localhost:27017/?w=-1")]
    [InlineData("mongodb://localhost:27017/?connectTimeoutMS=-5")]
    [InlineData("mongodb://localhost:27017/?maxPoolSize=1&minPoolSize=5")]
    [InlineData("mongodb://localhost:27017/?tls=true&ssl=false")]
    [InlineData("mongodb://user:p%ZZss@localhost:27017")]
    [InlineData("mongodb://localhost:27017/?authMechanism=NOPE")]
    [InlineData("mongodb://localhost:27017/?readPreference=bogus")]
    [InlineData("mongodb://localhost:27017/?srvMaxHosts=2")]
    [InlineData("mongodb://localhost:99999")]
    public async Task AddMongodb_WithAConnectionStringTheClientCannotBeBuiltFrom_FailsStartupAsValidation(
        string connection)
    {
        using var host = Host(connection, Given.DatabaseName);

        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());

        Assert.Contains(nameof(MongoOptions.Connection), exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The same refusal outside a host, where <see cref="Mongo"/> builds the client: the server's name rule is
    /// part of what makes settings valid, not a detail of the container.
    /// </summary>
    [Fact]
    public void Client_WithADatabaseNameTheServerRefuses_ThrowsOptionsValidationException()
    {
        var options = new MongoOptions { Connection = Given.Connection, Database = "a$b" };

        Assert.Throws<OptionsValidationException>(() => Mongo.Client(options));
    }

    /// <summary>
    /// Each key has a setup of its own, yet conventions land in one process-wide pack per name, and the driver's
    /// registry keeps the first pack under a name. The second key's conventions used to be dropped without a word —
    /// the audit database wrote <c>UserName</c> while its registration asked for camel case.
    /// </summary>
    [Fact]
    public void AddKeyedMongodb_WithConventionsUnderATakenPackName_ThrowsNamingTheKeyAndThePack()
    {
        var packName = $"test-{Guid.NewGuid():N}";

        try
        {
            var services = new ServiceCollection().AddKeyedMongodb("orders", Options("orders"), IgnoringExtras(packName));

            var exception = Assert.Throws<InvalidOperationException>(
                () => services.AddKeyedMongodb("audit", Options("audit"), CamelCasing(packName)));

            Assert.Contains("audit", exception.Message, StringComparison.Ordinal);
            Assert.Contains(packName, exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            ConventionRegistry.Remove(packName);
        }
    }

    [Fact]
    public void AddKeyedMongodb_WithAPackNamePerKey_AppliesBoth()
    {
        var first = $"test-{Guid.NewGuid():N}";
        var second = $"test-{Guid.NewGuid():N}";

        try
        {
            new ServiceCollection()
                .AddKeyedMongodb("orders", Options("orders"), IgnoringExtras(first))
                .AddKeyedMongodb("audit", Options("audit"), CamelCasing(second));

            Assert.Contains("\"userName\"", new AuditEntry { UserName = "ann" }.ToJson(), StringComparison.Ordinal);
        }
        finally
        {
            ConventionRegistry.Remove(first);
            ConventionRegistry.Remove(second);
        }
    }

    private static Action<MongoOptions> Options(string database) =>
        o =>
        {
            o.Connection = Given.Connection;
            o.Database = database;
        };

    private static Action<MongoSetup> IgnoringExtras(string packName) =>
        s =>
        {
            s.ConventionPackName = packName;
            s.Conventions = pack => pack.Add(new IgnoreExtraElementsConvention(true));
        };

    private static Action<MongoSetup> CamelCasing(string packName) =>
        s =>
        {
            s.ConventionPackName = packName;
            s.Conventions = pack => pack.Add(new CamelCaseElementNameConvention());
        };

    private sealed class AuditEntry
    {
        public string? UserName { get; set; }
    }
}
