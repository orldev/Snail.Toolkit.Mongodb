using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Snail.Toolkit.Mongodb.Diagnostics;

/// <summary>
/// Reports the database as healthy when it answers a command that requires authentication.
/// </summary>
/// <remarks>
/// The obvious probe is the wrong one. MongoDB answers <c>ping</c> without authentication, so a
/// connection string that lost its credentials reports Healthy while every query fails with
/// "Command insert requires authentication" — measured against mongo:8.0 with auth enabled, where a
/// credential-less client passed the ping check and failed every write. <c>listCollections</c> is
/// authenticated and scoped to this database, and <c>authorizedCollections</c> keeps it within the
/// privileges the application already holds.
/// </remarks>
/// <param name="database">The database to check.</param>
public sealed class MongoHealthCheck(IMongoDatabase database) : IHealthCheck
{
    private static readonly BsonDocument Probe = new()
    {
        { "listCollections", 1 },
        { "nameOnly", true },
        { "authorizedCollections", true }
    };

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var name = database.DatabaseNamespace.DatabaseName;

        try
        {
            var result = await database
                .RunCommandAsync<BsonDocument>(Probe, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return result.GetValue("ok", 0).ToDouble() is 1.0
                ? HealthCheckResult.Healthy($"MongoDB '{name}' answered an authenticated command.")
                : new HealthCheckResult(context.Registration.FailureStatus,
                    $"MongoDB '{name}' rejected the check: {result.ToJson()}");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new HealthCheckResult(
                context.Registration.FailureStatus, $"MongoDB '{name}' did not answer the check.", exception);
        }
    }
}
