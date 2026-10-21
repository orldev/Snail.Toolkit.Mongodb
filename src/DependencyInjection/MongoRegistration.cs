namespace Snail.Toolkit.Mongodb.DependencyInjection;

/// <summary>
/// Records that a key has been registered, and what it was registered from.
/// </summary>
/// <param name="ServiceKey">The key, or null for the unkeyed registration.</param>
/// <param name="SectionName">The configuration section the settings came from, if any.</param>
internal sealed record MongoRegistration(string? ServiceKey, string? SectionName);
