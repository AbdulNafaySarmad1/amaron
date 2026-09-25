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
    /// </summary>
    public static IQueryable<VariantSellable> Query(ICommerceDbContext db) => db.ProductVariants.Select(v => new VariantSellable
    {
        VariantId = v.Id,
        Units = db.WarehouseStocks.Any(s => s.VariantId == v.Id)
            ? Math.Min(v.Inventory.QuantityOnHand, db.WarehouseStocks.Where(s => s.VariantId == v.Id)
                .Sum(s => s.OnHand - s.Reserved - s.SafetyStock - s.Unavailable > 0 ? s.OnHand - s.Reserved - s.SafetyStock - s.Unavailable : 0))
            : v.Inventory.QuantityOnHand,
    });
}
