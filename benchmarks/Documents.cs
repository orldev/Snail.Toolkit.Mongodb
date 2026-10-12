namespace Snail.Toolkit.Mongodb.Benchmarks;

/// <summary>A document of a plain type, named after itself.</summary>
/// <param name="Id">The identifier.</param>
/// <param name="Customer">Who ordered.</param>
/// <param name="Quantity">How many.</param>
public sealed record Order(string Id, string Customer, int Quantity);

/// <summary>A document of a generic type, whose name unwraps its argument.</summary>
/// <param name="Id">The identifier.</param>
/// <param name="Body">The wrapped document.</param>
/// <typeparam name="T">The wrapped document's type.</typeparam>
public sealed record Envelope<T>(string Id, T Body);
