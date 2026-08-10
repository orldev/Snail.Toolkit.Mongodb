using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace Snail.Toolkit.Mongodb.Configuration;

/// <summary>
/// Settings used to connect to a MongoDB database.
/// </summary>
/// <remarks>
/// Bound from configuration through the options pattern, so it is kept to plain settable properties.
/// Anything configuration cannot carry — delegates and driver option objects — lives in
/// <see cref="MongoSetup"/>.
/// </remarks>
public sealed class MongoOptions
{
    /// <summary>
    /// The MongoDB connection string, for example <c>mongodb://user:password@localhost:27017</c>.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string? Connection { get; set; }

    /// <summary>
    /// The name of the database to use.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string? Database { get; set; }
}

/// <summary>
/// Validates <see cref="MongoOptions"/>. The implementation is generated from the data annotations
/// on the options, so validation costs no reflection at run time.
/// </summary>
[OptionsValidator]
public sealed partial class MongoOptionsValidator : IValidateOptions<MongoOptions>
{
    /// <summary>
    /// A shared instance, used to validate options built outside the container.
    /// </summary>
    public static MongoOptionsValidator Instance { get; } = new();

    /// <summary>
    /// Validates the options and throws when they are incomplete.
    /// </summary>
    /// <param name="options">The options to validate.</param>
    /// <param name="name">The name of the options instance, used in the error message.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
    /// <exception cref="OptionsValidationException">A required setting is missing.</exception>
    public static void ThrowIfInvalid(MongoOptions options, string name = "")
    {
        ArgumentNullException.ThrowIfNull(options);

        var result = Instance.Validate(name, options);

        if (result.Failed)
            throw new OptionsValidationException(name, typeof(MongoOptions), result.Failures);
    }
}
