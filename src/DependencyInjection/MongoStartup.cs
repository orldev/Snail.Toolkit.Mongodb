using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Snail.Toolkit.Mongodb.DependencyInjection;

/// <summary>
/// Builds every registered client when the host starts.
/// </summary>
/// <remarks>
/// Validation of the settings runs before this and can only ask the driver to parse. A value the driver accepts in
/// a connection string and refuses when the client is built — <c>connectTimeoutMS=-5</c> parses and the client's
/// constructor throws <see cref="ArgumentOutOfRangeException"/> — brought the host up and surfaced from the first
/// resolve. Building the clients here is the only way to ask, and it opens no connection: the driver's monitors
/// connect in the background. What the driver refuses is reported as the settings' own validation failure.
/// </remarks>
/// <param name="provider">The container.</param>
/// <param name="registrations">Every key registered.</param>
internal sealed class MongoStartup(IServiceProvider provider, IEnumerable<MongoRegistration> registrations)
    : IHostedService
{
    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var registration in registrations)
            Build(registration.ServiceKey);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private void Build(string? serviceKey)
    {
        try
        {
            if (serviceKey is null)
                provider.GetRequiredService<IMongoClient>();
            else
                provider.GetRequiredKeyedService<IMongoClient>(serviceKey);
        }
        catch (Exception exception) when (exception is ArgumentException or MongoException or NotSupportedException)
        {
            throw new OptionsValidationException(
                serviceKey ?? Options.DefaultName,
                typeof(MongoOptions),
                [$"Connection is not a connection string the MongoDB driver can build a client from: {exception.Message}"]);
        }
    }
}
