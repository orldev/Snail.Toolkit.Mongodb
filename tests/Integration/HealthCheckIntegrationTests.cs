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
        Assert.Contains("mongodb", report.Entries.Keys);
    }

    [Fact]
    public async Task AddMongo_WhenTheServerIsUnreachable_ReportsUnhealthyWithTheException()
    {
        await using var provider = Provider(Helper.UnreachableConnection);

        var report = await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync();

        Assert.Equal(HealthStatus.Unhealthy, report.Status);
        Assert.NotNull(report.Entries["mongodb"].Exception);
    }

    [Fact]
    public async Task AddMongo_HonoursTheGivenNameAndFailureStatus()
    {
        await using var provider = new ServiceCollection()
            .AddLogging()
            .AddMongodb(o =>
            {
                o.Connection = Helper.UnreachableConnection;
                o.Database = Helper.Database;
            })
            .AddHealthChecks()
            .AddMongodb("orders-db", HealthStatus.Degraded, "ready")
            .Services
            .BuildServiceProvider();

        var report = await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync();

        Assert.Equal(HealthStatus.Degraded, report.Status);
        Assert.Contains("ready", report.Entries["orders-db"].Tags);
    }
}
