using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Snail.Toolkit.Mongodb.DependencyInjection;

namespace Snail.Toolkit.Mongodb.Tests.Unit;

/// <summary>
/// The health check against a server that misbehaves and a client that cannot be built. Nothing here needs a server.
/// </summary>
public class HealthCheckChaosTests
{
    /// <summary>
    /// The health check service builds a check outside the guard that turns a failing check into an unhealthy
    /// entry, and <c>MongoUnregistered</c> stands in when no database is registered. A database that is registered
    /// but cannot be built — a client hook that throws — was the same hole: it took every other check of the
    /// report down with it, and the endpoint answered 500 naming nothing.
    /// </summary>
    [Fact]
    public async Task CheckHealthAsync_WhenTheClientHookThrows_ReportsThatCheckUnhealthyAndTheRestHealthy()
    {
        await using var provider = new ServiceCollection()
            .AddLogging()
            .AddMongodb(
                o =>
                {
                    o.Connection = Given.Connection;
                    o.Database = Given.DatabaseName;
                },
                s => s.ConfigureClient = _ => throw new IOException("The client certificate is missing."))
            .AddHealthChecks()
            .AddMongodb()
            .AddCheck("always", () => HealthCheckResult.Healthy())
            .Services
            .BuildServiceProvider();

        var report = await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync();

        Assert.Equal(HealthStatus.Unhealthy, report.Entries[MongoHealthChecksBuilderExtensions.DefaultName].Status);
        Assert.IsType<IOException>(report.Entries[MongoHealthChecksBuilderExtensions.DefaultName].Exception);
        Assert.Equal(HealthStatus.Healthy, report.Entries["always"].Status);
    }

    /// <summary>
    /// The same hole from the other side: settings that are incomplete in an application without a host, where
    /// nothing validated them at start-up, surfaced from the check's factory rather than as its entry.
    /// </summary>
    [Fact]
    public async Task CheckHealthAsync_WhenTheSettingsAreIncomplete_ReportsThatCheckUnhealthy()
    {
        await using var provider = new ServiceCollection()
            .AddLogging()
            .AddMongodb(o => o.Connection = Given.Connection)
            .AddHealthChecks()
            .AddMongodb()
            .Services
            .BuildServiceProvider();

        var report = await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync();

        Assert.Equal(HealthStatus.Unhealthy, report.Status);
    }

    /// <summary>
    /// A liveness probe is polled every few seconds with a timeout of about one. The driver's defaults wait 30 s for
    /// a server that accepts the connection and never answers, so the unbounded check held every probe of the
    /// process for half a minute; the registration's timeout bounds it.
    /// </summary>
    [Fact]
    public async Task CheckHealthAsync_AgainstAServerThatAcceptsAndNeverAnswers_ReportsUnhealthyWithinTheTimeout()
    {
        using var silent = new TcpListener(IPAddress.Loopback, 0);
        silent.Start();
        var port = ((IPEndPoint)silent.LocalEndpoint).Port;

        await using var provider = new ServiceCollection()
            .AddLogging()
            .AddMongodb(o =>
            {
                o.Connection = $"mongodb://127.0.0.1:{port}";
                o.Database = Given.DatabaseName;
            })
            .AddHealthChecks()
            .AddMongodb(timeout: TimeSpan.FromSeconds(1))
            .Services
            .BuildServiceProvider();

        var report = provider.GetRequiredService<HealthCheckService>().CheckHealthAsync();
        var first = await Task.WhenAny(report, Task.Delay(TimeSpan.FromSeconds(5)));

        Assert.Same(report, first);
        Assert.Equal(HealthStatus.Unhealthy, (await report).Status);
    }

    [Fact]
    public void AddKeyedMongodb_WithATimeout_RegistersIt()
    {
        var services = new ServiceCollection();
        services.AddHealthChecks().AddKeyedMongodb("orders", timeout: TimeSpan.FromSeconds(2));

        using var provider = services.BuildServiceProvider();
        var registration = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations.Single();

        Assert.Equal("orders", registration.Name);
        Assert.Equal(TimeSpan.FromSeconds(2), registration.Timeout);
    }
}
