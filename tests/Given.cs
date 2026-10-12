global using Xunit;
using Microsoft.Extensions.Configuration;
using MongoDB.Driver;

namespace Snail.Toolkit.Mongodb.Tests;

public static class Given
{
    public const string Section = "mongodb";
    public const string Connection = "mongodb://username:password@localhost:27017";
    public const string DatabaseName = "test";

    /// <summary>
    /// Points at a closed port with short timeouts, so operations fail quickly. Used to prove that
    /// driver failures surface as exceptions instead of being reported as a miss.
    /// </summary>
    public const string UnreachableConnection =
        "mongodb://127.0.0.1:1/?serverSelectionTimeoutMS=250&connectTimeoutMS=250";

    public static readonly Dictionary<string, string> InMemorySettings = new()
    {
        { $"{Section}:Connection", Connection },
        { $"{Section}:Database", DatabaseName }
    };

    public static IConfiguration Configuration() => Configuration(InMemorySettings);

    public static IConfiguration Configuration(Dictionary<string, string> settings) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(settings!)
            .Build();

    /// <summary>
    /// A database over a client that opens no connections until something is asked of it.
    /// </summary>
    public static IMongoDatabase Database(
        string connection = Connection,
        string database = DatabaseName,
        MongoSetup? setup = null) =>
        Mongo.Database(
            new MongoOptions { Connection = connection, Database = database }, setup);
}
