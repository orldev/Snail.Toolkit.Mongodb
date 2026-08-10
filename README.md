## Snail.Toolkit.Mongodb

Extension for the framework `MongoDB.Driver`

### What this is

A composition root plus extension methods. There is **no wrapper interface**: what goes into the
container is the driver's own `IMongoClient`, `IMongoDatabase` and `IGridFSBucket`, so a class asks
for exactly the one it uses — a repository that only reads takes `IMongoDatabase` and never sees a
client.

Everything the library adds is an extension method on those same types, so nothing has to be carried
around to reach it, each helper is opt-in, and there is no contract for an implementation or a test
double to keep in sync with the driver.

```c#
public class OrderRepository(IMongoDatabase db)          // no client, no wrapper
{
    public Task<Order?> GetAsync(string id, CancellationToken ct) =>
        db.GetCollection<Order>()                        // extension: naming convention
            .Find(Builders<Order>.Filter.Eq(x => x.Id, id))
            .FirstOrDefaultAsync(ct);
}
```

### Registration

```c#
builder.Services.AddMongodb(builder.Configuration);
```

reading the `mongo` section:

```json
{
  "mongodb": {
    "Connection": "mongodb://username:password@localhost:27017",
    "Database": "test"
  }
}
```

or configure it inline:

```c#
builder.Services.AddMongodb(options =>
{
    options.Connection = builder.Configuration["mongo:Connection"];
    options.Database = builder.Configuration["mongo:Database"];
});
```

That registers three singletons — `IMongoClient`, the configured `IMongoDatabase` off that same
client, and an `IGridFSBucket` over that database. The client is built on first resolve and disposed
by the container.

Settings go through the standard options pattern, so `IOptions<MongoOptions>` is resolvable and they
stay adjustable after registration:

```c#
builder.Services.AddMongodb(builder.Configuration);
builder.Services.Configure<MongoOptions>(o => o.Database = "tenant-42");
```

Validation is generated from the data annotations on `MongoOptions` — no reflection at run time — and
runs at host startup via `ValidateOnStart`. A missing setting refuses to bring the process up, with a
message naming the property, rather than surfacing on the first query.

Anything configuration cannot carry — delegates and driver option objects — goes in `setup`:

```c#
builder.Services.AddMongodb(builder.Configuration, sectionName: "storage", setup: s =>
{
    s.GridFsBucket = new GridFSBucketOptions { BucketName = "attachments" };
    s.ConfigureClient = settings => settings.ApplicationName = "my-app";
    s.Conventions = pack => pack.Add(new CamelCaseElementNameConvention());
});
```

`Conventions` go into the driver's `ConventionRegistry`, which is process-global: they affect every
client in the process, not just this registration.

Outside DI — a console app, a migration, a test — build the same things directly:

```c#
var db = MongoFactory.CreateDatabase(new MongoOptions { Connection = "...", Database = "test" });
```

#### Several databases

`AddMongodb` uses `TryAdd`, so it registers one set. For more than one database, register them under
keys — the key doubles as the options name, and each key gets its own client:

```c#
builder.Services
    .AddKeyedMongodb("orders", builder.Configuration, "mongo:orders")
    .AddKeyedMongodb("audit", builder.Configuration, "mongo:audit");

builder.Services.Configure<MongoOptions>("orders", o => o.Database = "orders-v2");

public class OrderRepository([FromKeyedServices("orders")] IMongoDatabase db);
```

### Collections

Without a name, `GetCollection<T>()` uses the naming convention — by default the type name with
generics unwrapped, so `Page<Order>` becomes `PageOfOrder`:

```c#
db.GetCollection<Order>();                            // "Order"
db.GetCollection<Order>("orders_v2");                 // explicit
db.GetCollection<Order>(naming: t => $"{t.Name}s");   // per call
MongoNaming.Default = t => $"{t.Name}s";              // process-wide, set once at start-up
```

The convention is process-wide, like the driver's own conventions — a document type maps to one
collection name, and threading that through every call site would cost more than it buys.

Filters, updates and sorts use the driver's `Builders<T>` directly. There are no aliases for them
here: they are static, they need no connection, and wrapping them only made query-building code
depend on something it never used.

### Aggregation

```c#
var orders = await db.AggregateToListAsync(pipeline, "orders", cancellationToken: ct);

await foreach (var order in db.AggregateAsAsyncEnumerable(pipeline, "orders", cancellationToken: ct))
    ...

var one = await db.AggregateSingleAsync(pipeline, "orders", cancellationToken: ct);
```

`AggregateToListAsync` buffers, `AggregateAsAsyncEnumerable` streams, and both dispose the cursor —
including when enumeration breaks out early. `AggregateSingleAsync` returns `null` for no match and
throws when the pipeline yields more than one document. `AggregateAsync` still returns the raw
`IAsyncCursor<T>` when you want it — that one is yours to dispose.

A pipeline reshapes documents, so input and output are separate type parameters. A `$lookup` plus
`$project` usually yields a shape with no model of its own, and there are two ways to say that:

```c#
// no model at either end — an inline array literal, no type arguments
var document = await db.AggregateSingleAsync(new BsonDocument[]
{
    new("$match", new BsonDocument { { "_id", id } }),
    new("$lookup", new BsonDocument
    {
        { "from", "User" }, { "localField", "ToWhom" }, { "foreignField", "_id" }, { "as", "User" }
    }),
    new("$unwind", "$User"),
    new("$project", new BsonDocument
    {
        { "DeviceToken", "$User.DeviceToken" }, { "Id", "Id" }
    })
}, "tenant-42");

// typed source, model-less result
PipelineDefinition<Order, BsonDocument> pipeline = stages;
var projected = await db.AggregateSingleAsync(pipeline, nameof(Order));
```

Every aggregation method has a plain `BsonDocument` overload alongside the generic one. That is what
makes the first form compile: C# cannot infer type arguments through the implicit conversion from
`BsonDocument[]` to `PipelineDefinition<,>`, so a generic-only API would force you to spell the type
arguments out on every call.

### Transactions

On `IMongoClient`:

```c#
await client.InTransactionAsync(async (session, ct) =>
{
    await db.GetCollection<Order>().InsertOneAsync(session, order, cancellationToken: ct);
    await db.GetCollection<Audit>().InsertOneAsync(session, entry, cancellationToken: ct);
});
```

The body is retried on transient transaction errors and committed when it returns; throwing rolls
back. Pass the `session` to every operation that must take part — work that ignores it is not in the
transaction. Requires a replica set or sharded cluster. A class holding only a database can reach the
client through `db.Client`.

### GridFS

One round trip for content and metadata:

```c#
await using var file = await bucket.OpenDownloadByObjectIdAsync(id, cancellationToken: ct);

if (file is null)
    return Results.NotFound();

return Results.Stream(file.Stream, file.ContentType ?? "application/octet-stream", file.Filename);
```

Or metadata only, without fetching content:

```c#
var found = await bucket.FindOneByObjectIdAsync(id, cancellationToken: ct);
```

Both return `null` when the file does not exist. A file without a `ContentType` metadata entry is
still returned, with `ContentType` set to `null`. Driver failures — an unreachable server, a
timeout — surface as exceptions rather than as a miss. The `string` overloads throw
`ArgumentException` on a malformed identifier; for untrusted input use `ObjectId.TryParse` and the
`ObjectId` overload. The metadata key defaults to `ContentType` and can be overridden per call.

Without DI, or for a second bucket in the same database, `db.GridFs()` builds one.

### Health checks

```c#
builder.Services.AddHealthChecks().AddMongodb();
builder.Services.AddHealthChecks().AddKeyedMongodb("orders");
```

Pings the registered database and reports the exception on failure. Accepts a name, a failure status
and tags.

### Tests

```bash
dotnet test
```

Part of the suite runs against real servers started with `Testcontainers.MongoDb` — a standalone
one, plus a single-node replica set for the transaction tests — so a running Docker daemon is
required.

## License

Snail.Toolkit.Mongodb is a free and open source project, released under the permissible [MIT license](LICENSE).