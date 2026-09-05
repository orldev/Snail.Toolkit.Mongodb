namespace Snail.Toolkit.Mongodb.Configuration;

/// <summary>
/// Knobs that cannot come from configuration — delegates and driver option objects. Kept apart
/// from <see cref="MongoOptions"/> so that type stays a plain, safely bindable POCO.
/// </summary>
public sealed class MongoSetup
{
    /// <summary>
    /// The name under which <see cref="Conventions"/> are registered in the driver's global
    /// <see cref="ConventionRegistry"/>.
    /// </summary>
    /// <remarks>
    /// Named after this package rather than something generic: the registry is shared by every
    /// library in the process, and a pack called <c>default</c> is one another library is liable to
    /// register — or remove — as its own.
    /// </remarks>
    public const string DefaultConventionPackName = "Snail.Toolkit.Mongodb";

    /// <summary>
    /// GridFS bucket options, such as bucket name or chunk size.
    /// </summary>
    public GridFSBucketOptions? GridFsBucket { get; set; }

    /// <summary>
    /// A hook over the <see cref="MongoClientSettings"/> parsed from the connection string, for TLS,
    /// timeouts, pool sizing, the LINQ provider, or subscribing to cluster and command events —
    /// which is how you wire up tracing or metrics.
    /// </summary>
    public Action<MongoClientSettings>? ConfigureClient { get; set; }

    /// <summary>
    /// Fills a <see cref="ConventionPack"/> applied to every mapped type — camel casing,
    /// ignoring extra elements, enum representation, and so on.
    /// </summary>
    /// <remarks>
    /// The driver's convention registry is process-global, so this is applied once per pack name
    /// and affects every client in the process, not just this registration.
    /// </remarks>
    public Action<ConventionPack>? Conventions { get; set; }

    /// <summary>
    /// The name <see cref="Conventions"/> are registered under.
    /// </summary>
    /// <remarks>
    /// It does not scope conventions to a client, and no name can: the driver's registry takes a type
    /// filter, not a client, so a pack applies to every mapped type in the process. Registering camel
    /// casing under one name and extra-element tolerance under another puts both on the same class
    /// map. Change this only to register a second, additional pack. A name is applied once per
    /// process and the first registration of it wins.
    /// </remarks>
    public string ConventionPackName { get; set; } = DefaultConventionPackName;
}
