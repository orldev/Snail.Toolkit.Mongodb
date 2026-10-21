using System.Buffers;
using Microsoft.Extensions.Options;

namespace Snail.Toolkit.Mongodb.Configuration;

/// <summary>
/// Refuses settings that are present and unusable: a connection string the driver cannot make a client of, and a
/// database name the server will not accept.
/// </summary>
/// <remarks>
/// <c>[Required]</c> only asks whether a string is there. The driver exposes no non-throwing parse, and it refuses a
/// value with whichever exception the setting's own parser throws — <see cref="MongoConfigurationException"/> for
/// most, <see cref="ArgumentOutOfRangeException"/> for <c>w=-1</c>, <see cref="NotSupportedException"/> for an
/// unknown <c>authMechanism</c> — so each of them becomes a failure naming the setting, with the driver's reason,
/// instead of escaping start-up as itself.
///
/// The server's rules for a database name are not in the driver, which refuses only a dot and a null character.
/// Measured against mongo:8.0: a slash, a backslash, a quote, a dollar sign and a space are refused with
/// "Invalid namespace", and 64 characters with "db name must be at most 63 characters".
/// </remarks>
internal sealed class MongoUsabilityValidator : IValidateOptions<MongoOptions>
{
    private const int LongestName = 63;

    private static readonly SearchValues<char> Refused = SearchValues.Create("/\\. \"$\0");

    /// <summary>
    /// A shared instance, used to validate options built outside the container.
    /// </summary>
    public static MongoUsabilityValidator Instance { get; } = new();

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, MongoOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>(2);

        if (!string.IsNullOrWhiteSpace(options.Connection) && Refusal(options.Connection) is { } reason)
            failures.Add($"Connection is not a connection string the MongoDB driver can use: {reason}");

        if (!string.IsNullOrWhiteSpace(options.Database) && IsRefusedByTheServer(options.Database))
            failures.Add(
                $"Database is not a legal MongoDB database name: at most {LongestName} characters, " +
                "none of / \\ . \" $, no space and no null character.");

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static string? Refusal(string connection)
    {
        try
        {
            MongoClientSettings.FromConnectionString(connection);

            return null;
        }
        catch (Exception exception)
            when (exception is MongoConfigurationException or ArgumentException or NotSupportedException or FormatException)
        {
            return exception.Message;
        }
    }

    private static bool IsRefusedByTheServer(string name) =>
        name.Length > LongestName || name.AsSpan().ContainsAny(Refused);
}
