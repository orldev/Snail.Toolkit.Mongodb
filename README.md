# Snail.Toolkit.Mongodb

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

reading the `Mongodb` section:

```json
{
  "Mongodb": {
    "Connection": "mongodb://username:password@localhost:27017",
    "Database": "test"
  }
}
```

or configure it inline:

```c#
builder.Services.AddMongodb(options =>
{
    options.Connection = builder.Configuration["Mongodb:Connection"];
    options.Database = builder.Configuration["Mongodb:Database"];
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
message naming the property, rather than surfacing on the first query. Present is not the same as
usable, so three more things are refused there, each with the driver's reason in the message: a
connection string the driver cannot parse, a database name the server will not accept (at most 63
characters, none of `/ \ . " $`, no space, no null character — what `mongo:8.0` refuses, which is more
than the driver checks), and a connection string the driver parses and then refuses when the client is
built, such as `connectTimeoutMS=-5`. That last one is caught by building every registered client when
the host starts; building one opens no connection, but an SRV connection string is resolved then, so
the start waits for DNS — about a second for a name that does not resolve. `ValidateOnStart` needs a
host to run in — outside one, a bare `ServiceCollection` or a console app, the same failures surface on
the first resolve instead.

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
client in the process, not just this registration, and `ConventionPackName` does not change that —
two packs under two names both land on the same document type. They are registered while the
container is being built, because a BSON class map is frozen the first time its type is serialised
and a convention arriving after that is silently ignored. A pack name is registered once per
process, and the first registration wins: calling `AddMongodb` twice with the same setup is a no-op,
while a second registration — another key, say — whose pack would land under a name already taken is
refused, naming the key and the pack, rather than having its conventions dropped. Give each key's
conventions a `ConventionPackName` of their own.

Outside DI — a console app, a migration, a test — build the same things directly:

```c#
var db = Mongo.Database(new MongoOptions { Connection = "...", Database = "test" });
```

#### Several databases

`AddMongodb` registers one set, and the first call is the one that stands — so a package can call it
defensively on an application that already did. Naming a *different* configuration section in a second
call is refused instead, with both section names in the message: that is not a repeated registration,
it is two databases asked of an API that registers one. Register those under keys — the key doubles as
the options name, and each key gets its own client:

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
MongoNaming.Use(t => $"{t.Name}s");                    // process-wide, chosen once at start-up
```

The convention is process-wide, like the driver's own conventions — a document type maps to one
collection name, and threading that through every call site would cost more than it buys. `Use`
accepts one call and refuses the rest: as a settable property, two components could each set it and
whichever ran second won without a word.

The driver builds a new collection object on every `GetCollection` call (about 2.6 KB). Called with
nothing but the document type, `GetCollection<T>()` keeps that object per database and type, so a
repository may ask for it per request: the call is then a lookup that allocates nothing. With a name,
a per-call convention or `MongoCollectionSettings`, every call reaches the driver and builds a new
one — hold such a collection in a field.

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

The helpers keep the collection they read per database, document type and name, so the driver's
collection object (about 2.6 KB) is built on the first call rather than on every one. Names made per
tenant or per day are fine: past 256 names a database stops keeping new ones and the driver builds
those per call, as it always did.

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
transaction. A token cancelled before the call does not run the body; one cancelled during a retry
stops it. Requires a replica set or sharded cluster. A class holding only a database can reach the
client through `db.Client`.

The aggregation helpers take the session too, so a read inside a transaction sees its own uncommitted
writes:

```c#
var pending = await db.AggregateToListAsync(pipeline, "orders", session: session, cancellationToken: ct);
```

The GridFS helpers do not, and cannot: `IGridFSBucket` declares no overload taking a session.

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

Without DI, or for a second bucket in the same database, `db.GridFs()` builds one — about 4.7 KB
and a check of the indexes on its first upload, so build it once and keep it.

### Health checks

```c#
builder.Services.AddHealthChecks().AddMongodb(timeout: TimeSpan.FromSeconds(1));
builder.Services.AddHealthChecks().AddKeyedMongodb("orders", timeout: TimeSpan.FromSeconds(1));
```

Runs `listCollections` against the registered database and reports the exception on failure. Accepts
a name, a failure status, a timeout and tags. A database that is registered but cannot be built — a
client hook that throws, settings refused outside a host — is reported as that check's own unhealthy
entry, with the exception, rather than failing the whole endpoint.

Give it a timeout. The driver's defaults wait 30 s for a server that accepts the connection and never
answers, while a liveness probe allows about one second; a socket that stalls after the handshake has
no timeout at all unless the connection string sets `socketTimeoutMS`. The timeout cancels the command
and the check reports unhealthy.

The probe is deliberately not `ping`: MongoDB answers a ping without authentication, so a connection
string that has lost its credentials passes a ping check while every query fails with `requires
authentication`. The check has to run something the server refuses to an unauthenticated client.

### Native AOT and trimming

The library is **not** for a Native AOT or trimmed application, and does not declare itself so. The
reason is its dependency rather than its own code: MongoDB.Driver and MongoDB.Bson 3.10 are not
trim- or AOT-safe. A native publish of a probe over this library warns IL2104 and IL3053 for both
assemblies, and the published executable then fails at run time — BSON class maps build serializers
by reflection and end in a `MissingMethodException`. Suppressing those warnings would hide a crash,
not prevent one. Use the library in a JIT application; it becomes a candidate for native use when
the driver itself supports it.

### Tests

```bash
dotnet test
```

Part of the suite runs against real servers started with `Testcontainers.MongoDb` — a standalone
one, plus a single-node replica set for the transaction tests — so a running Docker daemon is
required.

## License

Snail.Toolkit.Mongodb is a free and open source project, released under the permissible [MIT license](LICENSE).