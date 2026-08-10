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
    public const string DefaultConventionPackName = "default";

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
    /// The name used to register <see cref="Conventions"/>. Change it when a process hosts several
    /// clients that need different conventions.
    /// </summary>
    public string ConventionPackName { get; set; } = DefaultConventionPackName;
}
