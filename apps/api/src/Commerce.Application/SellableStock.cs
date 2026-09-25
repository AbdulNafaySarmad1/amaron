namespace Commerce.Application;

public sealed class VariantSellable
{
    public Guid VariantId { get; set; }
    public int Units { get; set; }
}

public static class SellableStock
{
    /// <summary>
    /// Units checkout can actually allocate, per variant. Mirrors CheckoutService: when any warehouse stocks the variant,
    /// the sum of each warehouse's available-to-sell (<see cref="OperationsCalculations.AvailableToSell"/>: on hand less
    /// reserved, safety stock and unavailable, never negative), capped by the aggregate on hand; otherwise the aggregate.
    /// Catalog availability, the "in stock" filter and the bag all use this, so nothing offered can be refused at checkout.
    /// Read from inventory (keyed by variant), not variants joined to themselves, so callers get one probe per variant.
    /// </summary>
    public static IQueryable<VariantSellable> Query(ICommerceDbContext db) => db.Inventory.Select(i => new VariantSellable
    {
        VariantId = i.VariantId,
        Units = db.WarehouseStocks.Any(s => s.VariantId == i.VariantId)
            ? Math.Min(i.QuantityOnHand, db.WarehouseStocks.Where(s => s.VariantId == i.VariantId).Sum(s => Math.Max(s.OnHand - s.Reserved - s.SafetyStock - s.Unavailable, 0)))
            : i.QuantityOnHand,
    });
}
