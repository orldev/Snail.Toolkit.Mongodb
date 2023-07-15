## MongoDB

#### Extension for the framework `MongoDB.Driver`

Connecting the configuration

```c# 
builder.Services.AddMongo(builder.Configuration);
```
or 
```c# 
builder.Services.AddSingleton<IMongoService>(new MongoService(options =>
{
    options.Connection = builder.Configuration["mongo:Connection"];
    options.Database = builder.Configuration["mongo:Database"];
}));
```