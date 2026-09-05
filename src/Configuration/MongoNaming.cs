namespace Snail.Toolkit.Mongodb.Configuration;

/// <summary>
/// Maps document types to collection names.
/// </summary>
/// <remarks>
/// The convention is process-wide, like the driver's own <see cref="ConventionRegistry"/>. Call
/// <see cref="Use"/> once during start-up, or pass a convention to a single call. It is deliberately
/// not a DI-scoped setting: a document type maps to one collection name, and threading that through
/// every call site or reviving a carrier object to hold it would cost more than it buys.
/// </remarks>
public static class MongoNaming
{
    private static Func<Type, string>? _chosen;

    /// <summary>
    /// The convention used when a call does not name a collection or pass its own.
    /// </summary>
    public static Func<Type, string> Default => _chosen ?? ForType;

    /// <summary>
    /// Chooses the process-wide convention, once.
    /// </summary>
    /// <remarks>
    /// This used to be a settable property, which meant two components could each set it and the
    /// one that ran second won silently — the collection a type mapped to then depended on module
    /// initialisation order. Setting it is a start-up decision and there is only one of them, so a
    /// second call is refused rather than obeyed.
    /// </remarks>
    /// <example>
    /// <code>
    /// MongoNaming.Use(type =&gt; $"{type.Name.ToLowerInvariant()}s");
    /// </code>
    /// </example>
    /// <param name="naming">Maps a document type to a collection name.</param>
    /// <exception cref="ArgumentNullException"><paramref name="naming"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The convention has already been chosen.</exception>
    public static void Use(Func<Type, string> naming)
    {
        ArgumentNullException.ThrowIfNull(naming);

        if (Interlocked.CompareExchange(ref _chosen, naming, null) is not null)
            throw new InvalidOperationException(
                "The collection naming convention is process-wide and is chosen once, at start-up. " +
                "Pass naming: to GetCollection to override it for a single call.");
    }

    /// <summary>
    /// Uses the type name, unwrapping generics into a readable form so that <c>Page&lt;Order&gt;</c>
    /// becomes <c>PageOfOrder</c> rather than the CLR's <c>Page`1</c>.
    /// </summary>
    /// <remarks>
    /// A type nested inside a generic one is itself generic and inherits the arguments, yet its name
    /// carries no arity marker: <c>Catalogue&lt;Order&gt;.Entry</c> is named <c>Entry</c>, and
    /// slicing at a backtick that is not there threw. The arguments still belong in the name, or
    /// <c>Catalogue&lt;Order&gt;.Entry</c> and <c>Catalogue&lt;User&gt;.Entry</c> would share one
    /// collection.
    /// </remarks>
    /// <param name="type">The document type.</param>
    /// <returns>The collection name.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="type"/> is null.</exception>
    public static string ForType(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        if (!type.IsGenericType)
            return type.Name;

        var arity = type.Name.IndexOf('`', StringComparison.Ordinal);
        var name = arity < 0 ? type.Name : type.Name[..arity];
        var arguments = type.GetGenericArguments().Select(ForType);

        return $"{name}Of{string.Join("And", arguments)}";
    }
}
