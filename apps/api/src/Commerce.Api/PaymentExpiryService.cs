using Commerce.Application;
using Commerce.Domain;
using Microsoft.EntityFrameworkCore;

namespace Commerce.Api;

public sealed class PaymentExpiryService(IServiceScopeFactory scopes, ILogger<PaymentExpiryService> logger, TimeProvider clock) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<ICommerceDbContext>();
                var now = clock.GetUtcNow();
                var payments = await db.Payments.Where(x => x.ExpiresAt <= now && (x.Status == PaymentStatus.Created || x.Status == PaymentStatus.Pending || x.Status == PaymentStatus.RequiresCustomerAction)).ToListAsync(stoppingToken);
                foreach (var payment in payments) { payment.Status = PaymentStatus.Expired; payment.UpdatedAt = now; }
                var reservations = await db.OrderInventoryReservations.Where(x => x.ExpiresAt <= now && x.ReleasedAt == null && x.FulfilledAt == null).ToListAsync(stoppingToken);
                if (reservations.Count > 0)
                {
                    var variants = reservations.Select(x => x.VariantId).Distinct().Order().ToArray();
                    await db.LockInventoryAsync(variants, stoppingToken);
                    var stocks = await db.WarehouseStocks.Where(x => reservations.Select(r => r.WarehouseId).Contains(x.WarehouseId) && variants.Contains(x.VariantId)).ToDictionaryAsync(x => (x.WarehouseId, x.VariantId), stoppingToken);
                    foreach (var row in reservations) { stocks[(row.WarehouseId, row.VariantId)].Reserved -= row.Quantity; stocks[(row.WarehouseId, row.VariantId)].UpdatedAt = now; row.ReleasedAt = now; }
                }
                if (payments.Count > 0 || reservations.Count > 0) await db.SaveChangesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception ex) { logger.LogError(ex, "Payment expiry sweep failed"); }
        }
    }
}
