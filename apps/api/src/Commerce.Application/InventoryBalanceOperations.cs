using Commerce.Domain;
using Microsoft.EntityFrameworkCore;

namespace Commerce.Application;

internal static class InventoryBalanceOperations
{
    public static async Task<InventoryBalance> EnsureUnscopedAvailableAsync(ICommerceDbContext db, WarehouseStock stock, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var unscoped = await db.InventoryBalances.SingleOrDefaultAsync(x => x.WarehouseId == stock.WarehouseId && x.VariantId == stock.VariantId && x.LocationId == null && x.LotId == null && x.State == InventoryState.Available, cancellationToken);
        if (unscoped is not null) return unscoped;

        var tracked = await db.InventoryBalances.Where(x => x.WarehouseId == stock.WarehouseId && x.VariantId == stock.VariantId && x.State == InventoryState.Available).SumAsync(x => (int?)x.Quantity, cancellationToken) ?? 0;
        if (tracked > stock.OnHand) throw CommerceErrors.Conflict("inventory_balance_inconsistent", "Available disposition balances exceed warehouse stock.");

        unscoped = new InventoryBalance
        {
            Id = Guid.CreateVersion7(), WarehouseId = stock.WarehouseId, VariantId = stock.VariantId,
            State = InventoryState.Available, Quantity = stock.OnHand - tracked, UpdatedAt = now
        };
        db.InventoryBalances.Add(unscoped);
        return unscoped;
    }

    public static async Task<IReadOnlyList<(InventoryBalance Balance, int Quantity)>> ConsumeAvailableAsync(ICommerceDbContext db, WarehouseStock stock, int quantity, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await EnsureUnscopedAvailableAsync(db, stock, now, cancellationToken);
        var balances = await db.InventoryBalances
            .Where(x => x.WarehouseId == stock.WarehouseId && x.VariantId == stock.VariantId && x.State == InventoryState.Available && x.Quantity > 0)
            .ToListAsync(cancellationToken);
        var lotIds = balances.Where(x => x.LotId.HasValue).Select(x => x.LotId!.Value).Distinct().ToArray();
        var lots = await db.InventoryLots.Where(x => lotIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, cancellationToken);
        balances = balances.OrderBy(x => x.LotId.HasValue ? lots[x.LotId.Value].ExpiresAt ?? DateTimeOffset.MaxValue : DateTimeOffset.MaxValue)
            .ThenBy(x => x.LotId.HasValue ? lots[x.LotId.Value].CreatedAt : DateTimeOffset.MaxValue)
            .ThenBy(x => x.Id).ToList();

        var remaining = quantity;
        var consumed = new List<(InventoryBalance, int)>();
        foreach (var balance in balances)
        {
            var take = Math.Min(balance.Quantity, remaining);
            if (take == 0) continue;
            balance.Quantity -= take;
            balance.UpdatedAt = now;
            consumed.Add((balance, take));
            remaining -= take;
            if (remaining == 0) break;
        }
        if (remaining > 0) throw CommerceErrors.Conflict("insufficient_available_stock", "Available disposition balances do not contain enough stock.");
        return consumed;
    }
}
