using Commerce.Application;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Commerce.Infrastructure;

public sealed class ResilientReadModelCache(HybridCache cache, ILogger<ResilientReadModelCache> logger) : IReadModelCache
{
    private long bypassUntilTicks;

    public async Task<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T>> factory, TimeSpan expiration, IReadOnlyCollection<string> tags, CancellationToken cancellationToken)
    {
        if (DateTimeOffset.UtcNow.UtcTicks < Interlocked.Read(ref bypassUntilTicks)) return await factory(cancellationToken);
        try
        {
            using var cacheBudget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cacheBudget.CancelAfter(TimeSpan.FromMilliseconds(300));
            return await cache.GetOrCreateAsync(key, factory, static (valueFactory, token) => new ValueTask<T>(valueFactory(token)), new HybridCacheEntryOptions { Expiration = expiration, LocalCacheExpiration = TimeSpan.FromSeconds(Math.Min(30, expiration.TotalSeconds)) }, tags, cacheBudget.Token);
        }
        catch (Exception ex) when (ex is RedisConnectionException or RedisTimeoutException or TimeoutException || ex is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            Interlocked.Exchange(ref bypassUntilTicks, DateTimeOffset.UtcNow.AddSeconds(10).UtcTicks);
            logger.LogWarning("Distributed cache unavailable; bypassing cache for {BypassSeconds} seconds", 10);
            return await factory(cancellationToken);
        }
    }

    public async Task RemoveByTagAsync(string tag, CancellationToken cancellationToken)
    {
        try { await cache.RemoveByTagAsync(tag, cancellationToken); }
        catch (Exception ex) when (ex is RedisConnectionException or RedisTimeoutException)
        {
            Interlocked.Exchange(ref bypassUntilTicks, DateTimeOffset.UtcNow.AddSeconds(10).UtcTicks);
            logger.LogWarning("Cache invalidation failed for tag {CacheTag}", tag);
        }
    }
}
