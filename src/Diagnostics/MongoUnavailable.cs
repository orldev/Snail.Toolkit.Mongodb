using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Snail.Toolkit.Mongodb.Diagnostics;

/// <summary>
/// Stands in for a check whose database is registered and could not be built.
/// </summary>
/// <remarks>
/// The same hole as <see cref="MongoUnregistered"/> from the other side: the check's factory runs outside the
/// health check service's guard, so a client hook that throws or settings refused on first resolve answered 500
/// for every check of the endpoint. This answers one unhealthy entry carrying the exception.
/// </remarks>
/// <param name="serviceKey">The key the database was registered under, or null for the unkeyed one.</param>
/// <param name="failure">What building the database threw.</param>
internal sealed class MongoUnavailable(string? serviceKey, Exception failure) : IHealthCheck
{
    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var which = serviceKey is null ? "The IMongoDatabase" : $"The IMongoDatabase under key '{serviceKey}'";
        var entry = new HealthCheckResult(
            context.Registration.FailureStatus, $"{which} could not be built from its settings.", failure);

        return Task.FromResult(entry);
    }
}
