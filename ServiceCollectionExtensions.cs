using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Toolkit.Mongo;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMongo(this IServiceCollection services, IConfiguration configuration)
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));

        var options = configuration
            .GetSection("mongo")
            .Get<MongoOptions>();
        
        if (options == null)
            throw new ArgumentNullException(nameof(options));
        
        services.AddSingleton<IMongoService>(new MongoService(options));

        return services;
    }
}