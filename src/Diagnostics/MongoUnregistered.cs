using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Snail.Toolkit.Mongodb.Diagnostics;

/// <summary>
/// Stands in for a check whose database was never registered.
/// </summary>
/// <remarks>
/// The health check service builds a check outside the guard that turns a failing check into an
/// unhealthy entry, so resolving a database that is not there threw straight out of the endpoint —
/// a 500 naming nothing, taking every other check with it. Reporting it as one unhealthy entry
/// keeps the rest of the report and says which registration is missing.
/// </remarks>
/// <param name="serviceKey">The key the database was expected under, or null for the unkeyed one.</param>
internal sealed class MongoUnregistered(string? serviceKey) : IHealthCheck
{
    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var message = serviceKey is null
            ? "No IMongoDatabase is registered. Call AddMongodb first."
            : $"No IMongoDatabase is registered under key '{serviceKey}'. Call AddKeyedMongodb(\"{serviceKey}\", ...) first.";

        return Task.FromResult(new HealthCheckResult(context.Registration.FailureStatus, message));
    }
}
