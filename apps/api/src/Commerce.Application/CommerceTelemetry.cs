using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Commerce.Application;

public static class CommerceTelemetry
{
    public const string Name = "Commerce";
    public static readonly ActivitySource ActivitySource = new(Name);
    public static readonly Meter Meter = new(Name);
    public static readonly Counter<long> CacheHits = Meter.CreateCounter<long>("commerce.cache.hits");
    public static readonly Counter<long> CacheMisses = Meter.CreateCounter<long>("commerce.cache.misses");
    public static readonly Counter<long> CacheFailures = Meter.CreateCounter<long>("commerce.cache.failures");
    public static readonly Counter<long> CheckoutFailures = Meter.CreateCounter<long>("commerce.checkout.failures");
    public static readonly Counter<long> OrdersCreated = Meter.CreateCounter<long>("commerce.orders.created");
    public static readonly Counter<long> RateLimitRejections = Meter.CreateCounter<long>("commerce.rate_limit.rejections");
    public static readonly Histogram<double> SearchDuration = Meter.CreateHistogram<double>("commerce.search.duration", "ms");
    public static readonly Histogram<double> CheckoutDuration = Meter.CreateHistogram<double>("commerce.checkout.duration", "ms");
}
