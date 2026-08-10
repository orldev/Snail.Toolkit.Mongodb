namespace Snail.Toolkit.Mongodb.Configuration;

/// <summary>
/// Maps document types to collection names.
/// </summary>
/// <remarks>
/// The convention is process-wide, like the driver's own <see cref="ConventionRegistry"/>. Set
/// <see cref="Default"/> once during start-up, or pass a convention to a single call. It is
/// deliberately not a DI-scoped setting: a document type maps to one collection name, and threading
/// that through every call site or reviving a carrier object to hold it would cost more than it buys.
/// </remarks>
public static class MongoNaming
{
    /// <summary>
    /// The convention used when a call does not name a collection or pass its own. Defaults to
    /// <see cref="ForType"/>.
    /// </summary>
    /// <example>
    /// <code>
    /// MongoNaming.Default = type => $"{type.Name.ToLowerInvariant()}s";
    /// </code>
    /// </example>
    public static Func<Type, string> Default { get; set; } = ForType;

    /// <summary>
    /// Uses the type name, unwrapping generics into a readable form so that <c>Page&lt;Order&gt;</c>
    /// becomes <c>PageOfOrder</c> rather than the CLR's <c>Page`1</c>.
    /// </summary>
    /// <param name="type">The document type.</param>
    /// <returns>The collection name.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="type"/> is null.</exception>
    public static string ForType(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        if (!type.IsGenericType)
            return type.Name;

        var name = type.Name[..type.Name.IndexOf('`')];
        var arguments = type.GetGenericArguments().Select(ForType);

        return $"{name}Of{string.Join("And", arguments)}";
    }
}
