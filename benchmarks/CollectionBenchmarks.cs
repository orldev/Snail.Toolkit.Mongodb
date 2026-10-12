using BenchmarkDotNet.Attributes;
using MongoDB.Driver;
using Snail.Toolkit.Mongodb.Configuration;
using Snail.Toolkit.Mongodb.Extensions;

namespace Snail.Toolkit.Mongodb.Benchmarks;

/// <summary>A collection named after its document type, as a repository asks for it per request.</summary>
/// <remarks>
/// The naming convention alone, and the whole call with the driver's collection object built behind it. The client
/// points at a closed port and is never asked to reach a server: building a collection does not talk to one.
/// </remarks>
[MemoryDiagnoser]
public class CollectionBenchmarks
{
    private MongoClient _client = null!;

    private IMongoDatabase _database = null!;

    /// <summary>A database of a client that has nothing to connect to.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _client = new MongoClient("mongodb://127.0.0.1:1/?serverSelectionTimeoutMS=100");
        _database = _client.GetDatabase("shop");
    }

    /// <summary>Releases the client.</summary>
    [GlobalCleanup]
    public void Cleanup() => _client.Dispose();

    /// <summary>The name of a plain type.</summary>
    /// <returns>The collection name.</returns>
    [Benchmark(Description = "MongoNaming.ForType(Order)")]
    public string NamePlain() => MongoNaming.ForType(typeof(Order));

    /// <summary>The name of a generic type.</summary>
    /// <returns>The collection name.</returns>
    [Benchmark(Description = "MongoNaming.ForType(Envelope<Order>)")]
    public string NameGeneric() => MongoNaming.ForType(typeof(Envelope<Order>));

    /// <summary>The collection of a plain type.</summary>
    /// <returns>The collection.</returns>
    [Benchmark(Description = "GetCollection<Order>()")]
    public object CollectionPlain() => _database.GetCollection<Order>();

    /// <summary>The collection of a generic type.</summary>
    /// <returns>The collection.</returns>
    [Benchmark(Description = "GetCollection<Envelope<Order>>()")]
    public object CollectionGeneric() => _database.GetCollection<Envelope<Order>>();
}
