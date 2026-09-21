using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Commerce.Contracts;
using Commerce.Domain;
using Microsoft.EntityFrameworkCore;

namespace Commerce.Application;

public sealed class CheckoutService(ICommerceDbContext db, IReadModelCache cache, TimeProvider clock)
{
    public async Task<CheckoutResultDto> ConfirmAsync(string customerId, string idempotencyKey, CheckoutRequest request, CancellationToken cancellationToken)
    {
        Validate(idempotencyKey, request);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request))));
        var existing = await db.IdempotencyRecords.AsNoTracking().SingleOrDefaultAsync(x => x.CustomerId == customerId && x.Key == idempotencyKey, cancellationToken);
        if (existing is not null) return await ReplayAsync(existing, hash, customerId, cancellationToken);

        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        var cartId = await db.Carts.AsNoTracking().Where(c => c.CustomerId == customerId).Select(c => (Guid?)c.Id).SingleOrDefaultAsync(cancellationToken)
            ?? throw CommerceErrors.Validation("The cart is empty.");
        await db.LockCartAsync(cartId, cancellationToken);

        existing = await db.IdempotencyRecords.AsNoTracking().SingleOrDefaultAsync(x => x.CustomerId == customerId && x.Key == idempotencyKey, cancellationToken);
        if (existing is not null) return await ReplayAsync(existing, hash, customerId, cancellationToken);
        // Mutable cart state must be read after the lock. A waiter must not act on
        // items tracked before another checkout committed and cleared the cart.
        var cart = await db.Carts.Include(c => c.Items).SingleAsync(c => c.Id == cartId, cancellationToken);
        if (cart.Items.Count == 0) throw CommerceErrors.Validation("The cart is empty.");

        var variantIds = cart.Items.Select(x => x.VariantId).Order().ToArray();
        await db.LockInventoryAsync(variantIds, cancellationToken);
        var variants = await db.ProductVariants.Include(v => v.Product).Include(v => v.Inventory)
            .Where(v => variantIds.Contains(v.Id)).ToDictionaryAsync(v => v.Id, cancellationToken);
        var warehouseStocks = await db.WarehouseStocks.Where(x => variantIds.Contains(x.VariantId)).OrderBy(x => x.WarehouseId).ToListAsync(cancellationToken);
        if (variants.Count != variantIds.Length) throw CommerceErrors.Conflict("product_unavailable", "A cart item is no longer available.");

        var currencies = variants.Values.Select(v => v.Currency).Distinct(StringComparer.Ordinal).ToArray();
        if (currencies.Length != 1) throw CommerceErrors.Conflict("mixed_currency", "All cart items must use one currency.");

        var now = clock.GetUtcNow();
        foreach (var item in cart.Items)
        {
            var variant = variants[item.VariantId];
            if (!variant.IsActive || variant.Product.Status != ProductStatus.Active) throw CommerceErrors.Conflict("product_unavailable", $"{variant.Product.Title} is no longer available.");
            if (variant.Inventory.QuantityOnHand < item.Quantity) throw CommerceErrors.Conflict("insufficient_inventory", $"Insufficient inventory for {variant.Product.Title}.");
        }

        var order = new Order
        {
            Id = Guid.CreateVersion7(),
            OrderNumber = $"AM-{now:yyyyMMdd}-{Guid.NewGuid():N}"[..20].ToUpperInvariant(),
            CustomerId = customerId,
            Status = OrderStatus.Placed,
            Currency = currencies[0],
            CreatedAt = now,
            Recipient = request.ShippingAddress.Recipient.Trim(),
            AddressLine1 = request.ShippingAddress.Line1.Trim(),
            AddressLine2 = request.ShippingAddress.Line2?.Trim(),
            City = request.ShippingAddress.City.Trim(),
            Region = request.ShippingAddress.Region.Trim(),
            PostalCode = request.ShippingAddress.PostalCode.Trim(),
            CountryCode = request.ShippingAddress.CountryCode.Trim().ToUpperInvariant()
        };
        foreach (var cartItem in cart.Items)
        {
            var variant = variants[cartItem.VariantId];
            variant.Inventory.QuantityOnHand -= cartItem.Quantity;
            variant.Inventory.UpdatedAt = now;
            var remaining = cartItem.Quantity;
            var variantStocks = warehouseStocks.Where(x => x.VariantId == variant.Id).ToList();
            foreach (var stock in variantStocks)
            {
                var available = OperationsCalculations.AvailableToSell(stock.OnHand, stock.Reserved, stock.SafetyStock, stock.Unavailable);
                var fulfilled = Math.Min(available, remaining);
                if (fulfilled == 0) continue;
                var consumed = await InventoryBalanceOperations.ConsumeAvailableAsync(db, stock, fulfilled, now, cancellationToken);
                stock.OnHand -= fulfilled; stock.UpdatedAt = now; remaining -= fulfilled;
                foreach (var allocation in consumed)
                    db.InventoryLedgerEntries.Add(new InventoryLedgerEntry { Id = Guid.CreateVersion7(), VariantId = variant.Id, WarehouseId = stock.WarehouseId, LocationId = allocation.Balance.LocationId, LotId = allocation.Balance.LotId, State = InventoryState.Available, QuantityDelta = -allocation.Quantity, Reason = InventoryMovementReason.OrderFulfilled, ReferenceType = "Order", ReferenceId = order.Id.ToString(), CreatedBy = "checkout", CreatedAt = now });
                if (remaining == 0) break;
            }
            if (variantStocks.Count > 0 && remaining > 0) throw CommerceErrors.Conflict("insufficient_available_stock", $"Insufficient available warehouse stock for {variant.Product.Title}.");
            var unitPrice = variant.Price;
            order.Items.Add(new OrderItem { Id = Guid.CreateVersion7(), OrderId = order.Id, VariantId = variant.Id, Sku = variant.Sku, ProductTitle = variant.Product.Title, VariantName = variant.Name, UnitPrice = unitPrice, Quantity = cartItem.Quantity, LineTotal = unitPrice * cartItem.Quantity });
        }
        order.Subtotal = order.Items.Sum(x => x.LineTotal);
        db.Orders.Add(order);
        db.CartItems.RemoveRange(cart.Items);
        db.IdempotencyRecords.Add(new IdempotencyRecord { Id = Guid.CreateVersion7(), CustomerId = customerId, Key = idempotencyKey, RequestHash = hash, OrderId = order.Id, CreatedAt = now });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await cache.RemoveByTagAsync("products", CancellationToken.None);
        await cache.RemoveByTagAsync("homepage", CancellationToken.None);
        return new CheckoutResultDto(MapOrder(order), false);
    }

    public async Task<OrderDto> GetOrderAsync(string customerId, Guid id, bool canReadAny, CancellationToken cancellationToken)
    {
        var order = await db.Orders.AsNoTracking().Include(o => o.Items).SingleOrDefaultAsync(o => o.Id == id && (canReadAny || o.CustomerId == customerId), cancellationToken);
        return order is null ? throw CommerceErrors.NotFound("Order") : MapOrder(order);
    }

    public async Task<IReadOnlyList<OrderDto>> GetOrdersAsync(string customerId, int pageSize, bool canReadAny, CancellationToken cancellationToken)
    {
        if (pageSize is < 1 or > 50) throw CommerceErrors.Validation("pageSize must be between 1 and 50.");
        var query = db.Orders.AsNoTracking().Include(o => o.Items).AsQueryable();
        if (!canReadAny) query = query.Where(o => o.CustomerId == customerId);
        var orders = await query.OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id).Take(pageSize).ToListAsync(cancellationToken);
        return orders.Select(MapOrder).ToList();
    }

    private async Task<CheckoutResultDto> ReplayAsync(IdempotencyRecord record, string hash, string customerId, CancellationToken cancellationToken)
    {
        if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(record.RequestHash), Convert.FromHexString(hash))) throw CommerceErrors.Conflict("idempotency_key_reused", "The idempotency key was already used with a different request.");
        return new CheckoutResultDto(await GetOrderAsync(customerId, record.OrderId, false, cancellationToken), true);
    }

    private static OrderDto MapOrder(Order order) => new(order.Id, order.OrderNumber, order.Status.ToString(), new MoneyDto(order.Subtotal, order.Currency), order.CreatedAt,
        order.Items.OrderBy(i => i.ProductTitle).Select(i => new OrderItemDto(i.VariantId, i.Sku, i.ProductTitle, i.VariantName, i.Quantity, new MoneyDto(i.UnitPrice, order.Currency), new MoneyDto(i.LineTotal, order.Currency))).ToList());

    private static void Validate(string key, CheckoutRequest request)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 128 || key.Any(char.IsControl)) throw CommerceErrors.Validation("A valid Idempotency-Key header is required.");
        var a = request.ShippingAddress;
        if (string.IsNullOrWhiteSpace(a.Recipient) || a.Recipient.Length > 120 || string.IsNullOrWhiteSpace(a.Line1) || a.Line1.Length > 200 || string.IsNullOrWhiteSpace(a.City) || a.City.Length > 100 || string.IsNullOrWhiteSpace(a.Region) || a.Region.Length > 100 || string.IsNullOrWhiteSpace(a.PostalCode) || a.PostalCode.Length > 24 || a.CountryCode.Length != 2)
            throw CommerceErrors.Validation("The shipping address is invalid.");
    }
}
