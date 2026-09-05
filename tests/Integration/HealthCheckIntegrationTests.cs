using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Snail.Toolkit.Mongodb.DependencyInjection;

namespace Snail.Toolkit.Mongodb.Tests.Integration;

[Collection(MongoContainerCollection.Name)]
public class HealthCheckIntegrationTests(MongoContainerFixture fixture)
{
    private static ServiceProvider Provider(string connection) =>
        new ServiceCollection()
            .AddLogging()
            .AddMongodb(o =>
            {
                o.Connection = connection;
                o.Database = MongoContainerFixture.NewDatabaseName();
            })
            .AddHealthChecks()
            .AddMongodb()
            .Services
            .BuildServiceProvider();

    [Fact]
    public async Task AddMongo_WhenTheServerAnswers_ReportsHealthy()
    {
        await using var provider = Provider(fixture.Connection);

        var report = await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync();

        Assert.Equal(HealthStatus.Healthy, report.Status);
        Assert.Contains(MongoHealthChecksBuilderExtensions.DefaultName, report.Entries.Keys);
    }

    [Fact]
    public async Task AddMongo_WhenTheServerIsUnreachable_ReportsUnhealthyWithTheException()
    {
        await using var provider = Provider(Given.UnreachableConnection);

        var report = await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync();

        Assert.Equal(HealthStatus.Unhealthy, report.Status);
        Assert.NotNull(report.Entries[MongoHealthChecksBuilderExtensions.DefaultName].Exception);
    }

    /// <summary>
    /// A ping is answered without authentication, so the previous check reported Healthy for a
    /// connection string that had lost its credentials while every query failed. The probe has to be
    /// a command the server refuses to an unauthenticated client.
    /// </summary>
    [Fact]
    public async Task AddMongodb_WhenCredentialsAreMissing_ReportsUnhealthy()
    {
        await using var provider = Provider(WithoutCredentials(fixture.Connection));

        var report = await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync();

        Assert.Equal(HealthStatus.Unhealthy, report.Status);
    }

    [Fact]
    public async Task AddMongo_HonoursTheGivenNameAndFailureStatus()
    {
        await using var provider = new ServiceCollection()
            .AddLogging()
            .AddMongodb(o =>
            {
                o.Connection = Given.UnreachableConnection;
                o.Database = Given.DatabaseName;
            })
            .AddHealthChecks()
            .AddMongodb("orders-db", HealthStatus.Degraded, "ready")
            .Services
            .BuildServiceProvider();

        var report = await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync();

        Assert.Equal(HealthStatus.Degraded, report.Status);
        Assert.Contains("ready", report.Entries["orders-db"].Tags);
    }

    /// <summary>
    /// The keyed check had no test at all — Roslyn reported it as referenced by nothing in the
    /// solution.
    /// </summary>
    [Fact]
    public async Task AddKeyedMongodb_WhenTheServerAnswers_ReportsHealthy()
    {
        await using var provider = new ServiceCollection()
            .AddLogging()
            .AddKeyedMongodb("orders", o =>
            {
                o.Connection = fixture.Connection;
                o.Database = MongoContainerFixture.NewDatabaseName();
            })
            .AddHealthChecks()
            .AddKeyedMongodb("orders")
            .Services
            .BuildServiceProvider();

        var report = await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync();

        Assert.Equal(HealthStatus.Healthy, report.Status);
        Assert.Contains("orders", report.Entries.Keys);
    }

    /// <summary>
    /// Resolving a database that was never registered threw straight out of the endpoint, so one
    /// misconfigured check answered 500 for every other check too.
    /// </summary>
    [Fact]
    public async Task AddMongodb_WhenNoDatabaseIsRegistered_ReportsUnhealthyNamingTheFix()
    {
        await using var provider = new ServiceCollection()
            .AddLogging()
            .AddKeyedMongodb("orders", o =>
            {
                o.Connection = fixture.Connection;
                o.Database = MongoContainerFixture.NewDatabaseName();
            })
            .AddHealthChecks()
            .AddMongodb()
            .Services
            .BuildServiceProvider();

        var report = await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync();

        Assert.Equal(HealthStatus.Unhealthy, report.Status);
        Assert.Contains(
            nameof(MongoServiceCollectionExtensions.AddMongodb),
            report.Entries[MongoHealthChecksBuilderExtensions.DefaultName].Description,
            StringComparison.Ordinal);
    }

    private static string WithoutCredentials(string connection)
    {
        const string scheme = "mongodb://";

        var rest = connection[scheme.Length..];
        var credentials = rest.IndexOf('@', StringComparison.Ordinal);

        return credentials < 0 ? connection : $"{scheme}{rest[(credentials + 1)..]}";
    }
}
