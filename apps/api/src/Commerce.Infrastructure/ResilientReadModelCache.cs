using Commerce.Application;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Commerce.Infrastructure;

public sealed class ResilientReadModelCache(HybridCache cache, ILogger<ResilientReadModelCache> logger, IConfiguration configuration) : IReadModelCache
{
    private long bypassUntilTicks;
    private readonly TimeSpan cacheOperationTimeout = TimeSpan.FromMilliseconds(
        int.TryParse(configuration["CACHE_OPERATION_TIMEOUT_MS"], out var configuredTimeout) ? Math.Clamp(configuredTimeout, 50, 5_000) : 300);

    public async Task<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T>> factory, TimeSpan expiration, IReadOnlyCollection<string> tags, CancellationToken cancellationToken)
    {
        if (DateTimeOffset.UtcNow.UtcTicks < Interlocked.Read(ref bypassUntilTicks))
        {
            CommerceTelemetry.CacheMisses.Add(1, new KeyValuePair<string, object?>("cache.layer", "bypass"));
            return await factory(cancellationToken);
        }
        var databaseRead = new Lazy<Task<T>>(() => factory(cancellationToken), LazyThreadSafetyMode.ExecutionAndPublication);
        try
        {
            var factoryInvoked = false;
            // HybridCache shares its cancellation token with the factory. Keep the
            // database read on the endpoint budget and memoize it so an L2 timeout
            // cannot cancel or duplicate an in-flight cache-fill query.
            using var cacheBudget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cacheBudget.CancelAfter(cacheOperationTimeout);
            var result = await cache.GetOrCreateAsync(key, async token =>
            {
                factoryInvoked = true;
                return await databaseRead.Value;
            }, new HybridCacheEntryOptions { Expiration = expiration, LocalCacheExpiration = TimeSpan.FromSeconds(Math.Min(5, expiration.TotalSeconds)) }, tags, cacheBudget.Token);
            (factoryInvoked ? CommerceTelemetry.CacheMisses : CommerceTelemetry.CacheHits).Add(1);
            return result;
        }
        catch (Exception ex) when (ex is RedisConnectionException or RedisTimeoutException or TimeoutException || ex is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            Interlocked.Exchange(ref bypassUntilTicks, DateTimeOffset.UtcNow.AddSeconds(10).UtcTicks);
            CommerceTelemetry.CacheFailures.Add(1);
            logger.LogWarning(ex, "Distributed cache unavailable; bypassing cache for {BypassSeconds} seconds", 10);
            return await databaseRead.Value;
        }
    }

    public async Task RemoveByTagAsync(string tag, CancellationToken cancellationToken)
    {
        try { await cache.RemoveByTagAsync(tag, cancellationToken); }
        catch (Exception ex)
        {
            Interlocked.Exchange(ref bypassUntilTicks, DateTimeOffset.UtcNow.AddSeconds(10).UtcTicks);
            CommerceTelemetry.CacheFailures.Add(1);
            logger.LogWarning(ex, "Best-effort cache invalidation failed for tag {CacheTag}", tag);
        }
    }
}
