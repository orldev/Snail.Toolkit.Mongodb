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
    public const string DefaultName = "mongodb";

    /// <summary>
    /// Adds a check that pings the <see cref="IMongoDatabase"/> registered by <c>AddMongo</c>.
    /// </summary>
    /// <param name="builder">The health checks builder.</param>
    /// <param name="name">The name of the check.</param>
    /// <param name="failureStatus">The status to report on failure. Defaults to <see cref="HealthStatus.Unhealthy"/>.</param>
    /// <param name="tags">Tags used to filter which checks an endpoint runs.</param>
    /// <returns>The builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is null.</exception>
    public static IHealthChecksBuilder AddMongodb(
        this IHealthChecksBuilder builder,
        string name = DefaultName,
        HealthStatus? failureStatus = null,
        params string[] tags)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return builder.AddCheck<MongoHealthCheck>(name, failureStatus, tags);
    }

    /// <summary>
    /// Adds a check that pings a keyed <see cref="IMongoDatabase"/> registered by <c>AddKeyedMongo</c>.
    /// </summary>
    /// <param name="builder">The health checks builder.</param>
    /// <param name="serviceKey">The key the database was registered under.</param>
    /// <param name="name">The name of the check. Defaults to the key.</param>
    /// <param name="failureStatus">The status to report on failure. Defaults to <see cref="HealthStatus.Unhealthy"/>.</param>
    /// <param name="tags">Tags used to filter which checks an endpoint runs.</param>
    /// <returns>The builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is null.</exception>
    public static IHealthChecksBuilder AddKeyedMongodb(
        this IHealthChecksBuilder builder,
        string serviceKey,
        string? name = null,
        HealthStatus? failureStatus = null,
        params string[] tags)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceKey);

        return builder.Add(new HealthCheckRegistration(
            name ?? serviceKey,
            sp => new MongoHealthCheck(sp.GetRequiredKeyedService<IMongoDatabase>(serviceKey)),
            failureStatus,
            tags));
    }
}
