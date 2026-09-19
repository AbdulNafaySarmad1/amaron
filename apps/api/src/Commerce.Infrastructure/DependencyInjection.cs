using Commerce.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Commerce.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddCommerceInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var database = configuration["DATABASE_URL"] ?? configuration.GetConnectionString("Commerce") ?? "Host=localhost;Port=5432;Database=commerce;Username=commerce;Password=commerce_dev";
        // Transactional checkout is intentionally not wrapped in an implicit retry
        // strategy; ambiguous commits are resolved by the persisted idempotency key.
        services.AddDbContext<CommerceDbContext>(options => options.UseNpgsql(database, npgsql => npgsql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)));
        services.AddScoped<ICommerceDbContext>(sp => sp.GetRequiredService<CommerceDbContext>());

        var cacheEnabled = !bool.TryParse(configuration["CACHE_ENABLED"], out var configuredCacheEnabled) || configuredCacheEnabled;
        var cacheProvider = configuration["CACHE_PROVIDER"]?.ToLowerInvariant() ?? "valkey";
        if (cacheEnabled && cacheProvider is "valkey" or "redis")
        {
            services.AddStackExchangeRedisCache(options => options.Configuration = configuration["CACHE_CONNECTION"] ?? "localhost:6379,connectTimeout=500,abortConnect=false");
        }
        else services.AddDistributedMemoryCache();
        services.AddHybridCache(options => { options.MaximumPayloadBytes = 1024 * 1024; options.MaximumKeyLength = 256; });
        services.AddSingleton<IReadModelCache, ResilientReadModelCache>();
        return services;
    }
}
