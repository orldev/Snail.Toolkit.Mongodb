using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Snail.Toolkit.Mongodb.Diagnostics;

/// <summary>
/// Reports the database as healthy when it answers a <c>ping</c>.
/// </summary>
/// <param name="database">The database to check.</param>
public sealed class MongoHealthCheck(IMongoDatabase database) : IHealthCheck
{
    private static readonly BsonDocument Ping = new("ping", 1);

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
                .RunCommandAsync<BsonDocument>(Ping, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return result.GetValue("ok", 0).ToDouble() is 1.0
                ? HealthCheckResult.Healthy($"MongoDB '{name}' answered a ping.")
                : new HealthCheckResult(context.Registration.FailureStatus,
                    $"MongoDB '{name}' rejected a ping: {result.ToJson()}");
        }
        catch (Exception exception)
        {
            return new HealthCheckResult(
                context.Registration.FailureStatus, $"MongoDB '{name}' did not answer a ping.", exception);
        }
    }
}
