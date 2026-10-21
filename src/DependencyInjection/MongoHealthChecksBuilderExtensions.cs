using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Snail.Toolkit.Mongodb.Diagnostics;

namespace Snail.Toolkit.Mongodb.DependencyInjection;

/// <summary>
/// Registers the MongoDB health check.
/// </summary>
public static class MongoHealthChecksBuilderExtensions
{
    /// <summary>
    /// The name the check is registered under when none is given.
    /// </summary>
    public const string DefaultName = "Mongodb";

    /// <summary>
    /// Adds a check over the <see cref="IMongoDatabase"/> registered by <c>AddMongodb</c>.
    /// </summary>
    /// <remarks>
    /// A liveness probe is polled every few seconds with a timeout of about one. The driver's defaults wait 30 s for
    /// a server that accepts the connection and never answers, and a socket that stalls after the handshake has no
    /// timeout at all: measured against a listener that accepts and never replies, a check without
    /// <paramref name="timeout"/> was still waiting after 5 s. The timeout cancels the command and the check reports
    /// unhealthy.
    ///
    /// The overload without defaults is the shape of the first release, kept for whoever compiled against it; this
    /// one, with every default, is the one a new call binds to (RS0026, RS0027).
    /// </remarks>
    /// <param name="builder">The health checks builder.</param>
    /// <param name="name">The name of the check.</param>
    /// <param name="failureStatus">The status to report on failure. Defaults to <see cref="HealthStatus.Unhealthy"/>.</param>
    /// <param name="timeout">How long the check may take before it is reported unhealthy, or null for no bound.</param>
    /// <param name="tags">Tags used to filter which checks an endpoint runs.</param>
    /// <returns>The builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is null.</exception>
    public static IHealthChecksBuilder AddMongodb(
        this IHealthChecksBuilder builder,
        string name = DefaultName,
        HealthStatus? failureStatus = null,
        TimeSpan? timeout = null,
        params string[] tags)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return builder.Add(Registration(name, serviceKey: null, failureStatus, tags, timeout));
    }

    /// <summary>
    /// Adds a check over the <see cref="IMongoDatabase"/> registered by <c>AddMongodb</c>, without a timeout.
    /// </summary>
    /// <param name="builder">The health checks builder.</param>
    /// <param name="name">The name of the check.</param>
    /// <param name="failureStatus">The status to report on failure, or null for <see cref="HealthStatus.Unhealthy"/>.</param>
    /// <param name="tags">Tags used to filter which checks an endpoint runs.</param>
    /// <returns>The builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is null.</exception>
    public static IHealthChecksBuilder AddMongodb(
        this IHealthChecksBuilder builder,
        string name,
        HealthStatus? failureStatus,
        params string[] tags) =>
        builder.AddMongodb(name, failureStatus, timeout: null, tags);

    /// <summary>
    /// Adds a check over a keyed <see cref="IMongoDatabase"/> registered by <c>AddKeyedMongodb</c>.
    /// </summary>
    /// <param name="builder">The health checks builder.</param>
    /// <param name="serviceKey">The key the database was registered under.</param>
    /// <param name="name">The name of the check. Defaults to the key.</param>
    /// <param name="failureStatus">The status to report on failure. Defaults to <see cref="HealthStatus.Unhealthy"/>.</param>
    /// <param name="timeout">How long the check may take before it is reported unhealthy, or null for no bound.</param>
    /// <param name="tags">Tags used to filter which checks an endpoint runs.</param>
    /// <returns>The builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is null.</exception>
    public static IHealthChecksBuilder AddKeyedMongodb(
        this IHealthChecksBuilder builder,
        string serviceKey,
        string? name = null,
        HealthStatus? failureStatus = null,
        TimeSpan? timeout = null,
        params string[] tags)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceKey);

        return builder.Add(Registration(name ?? serviceKey, serviceKey, failureStatus, tags, timeout));
    }

    /// <summary>
    /// Adds a check over a keyed <see cref="IMongoDatabase"/> registered by <c>AddKeyedMongodb</c>, without a timeout.
    /// </summary>
    /// <param name="builder">The health checks builder.</param>
    /// <param name="serviceKey">The key the database was registered under.</param>
    /// <param name="name">The name of the check, or null for the key.</param>
    /// <param name="failureStatus">The status to report on failure, or null for <see cref="HealthStatus.Unhealthy"/>.</param>
    /// <param name="tags">Tags used to filter which checks an endpoint runs.</param>
    /// <returns>The builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is null.</exception>
    public static IHealthChecksBuilder AddKeyedMongodb(
        this IHealthChecksBuilder builder,
        string serviceKey,
        string? name,
        HealthStatus? failureStatus,
        params string[] tags) =>
        builder.AddKeyedMongodb(serviceKey, name, failureStatus, timeout: null, tags);

    /// <summary>
    /// Resolves the database when the check runs rather than when it is added, so the two can be
    /// registered in either order.
    /// </summary>
    /// <remarks>
    /// The health check service calls this outside the guard that turns a failing check into an unhealthy entry,
    /// so a database that cannot be built — a client hook that throws, settings refused in an application without
    /// a host — threw out of the endpoint and took every other check with it. The failure is answered as the
    /// check's own entry instead, with the exception.
    /// </remarks>
    private static HealthCheckRegistration Registration(
        string name,
        string? serviceKey,
        HealthStatus? failureStatus,
        string[] tags,
        TimeSpan? timeout) =>
        new(name, provider => Check(provider, serviceKey), failureStatus, tags, timeout);

    private static IHealthCheck Check(IServiceProvider provider, string? serviceKey)
    {
        try
        {
            var database = serviceKey is null
                ? provider.GetService<IMongoDatabase>()
                : provider.GetKeyedService<IMongoDatabase>(serviceKey);

            return database is null ? new MongoUnregistered(serviceKey) : new MongoHealthCheck(database);
        }
        catch (Exception failure)
        {
            return new MongoUnavailable(serviceKey, failure);
        }
    }
}
