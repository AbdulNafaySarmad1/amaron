using Commerce.Contracts;
using Commerce.Domain;
using Microsoft.EntityFrameworkCore;

namespace Commerce.Application;

public sealed class CartService(ICommerceDbContext db, TimeProvider clock)
{
    public async Task<CartDto> GetAsync(string customerId, CancellationToken cancellationToken)
    {
        var cart = await QueryCart(customerId).SingleOrDefaultAsync(cancellationToken);
        return cart is null ? EmptyCart() : Map(cart);
    }

    public async Task<CartSummaryDto> GetSummaryAsync(string customerId, CancellationToken cancellationToken)
    {
        var cart = await QueryCart(customerId).SingleOrDefaultAsync(cancellationToken);
        var mapped = cart is null ? EmptyCart() : Map(cart);
        return new CartSummaryDto(cart?.Id, mapped.TotalQuantity, mapped.Subtotal, cart is null ? null : mapped.Version);
    }

    public async Task<CartMutationDto> SetItemAsync(string customerId, SetCartItemRequest request, CancellationToken cancellationToken)
    {
        if (request.Quantity is < 1 or > 99) throw CommerceErrors.Validation("Quantity must be between 1 and 99.");
        var variant = await db.ProductVariants.AsNoTracking().Where(v => v.Id == request.VariantId && v.IsActive && v.Product.Status == ProductStatus.Active)
            .Select(v => new { v.Id, Available = v.Inventory.QuantityOnHand }).SingleOrDefaultAsync(cancellationToken)
            ?? throw CommerceErrors.NotFound("Variant");
        if (variant.Available < request.Quantity) throw CommerceErrors.Conflict("insufficient_inventory", "The requested quantity is not currently available.");

        var cart = await db.Carts.Include(c => c.Items).SingleOrDefaultAsync(c => c.CustomerId == customerId, cancellationToken);
        if (cart is null)
        {
            cart = new Cart { Id = Guid.CreateVersion7(), CustomerId = customerId, UpdatedAt = clock.GetUtcNow() };
            db.Carts.Add(cart);
        }
        else
        {
            await db.LockCartAsync(cart.Id, cancellationToken);
        }

        var item = cart.Items.SingleOrDefault(x => x.VariantId == request.VariantId);
        if (item is null)
        {
            item = new CartItem { CartId = cart.Id, VariantId = request.VariantId, Quantity = request.Quantity, AddedAt = clock.GetUtcNow() };
            cart.Items.Add(item);
        }
        else item.Quantity = request.Quantity;
        cart.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);

        var result = await GetAsync(customerId, cancellationToken);
        return new CartMutationDto(result.CartId, result.TotalQuantity, result.Subtotal, result.Items.Single(x => x.VariantId == request.VariantId), result.Version);
    }

    public async Task<CartDto> RemoveItemAsync(string customerId, Guid variantId, CancellationToken cancellationToken)
    {
        var cart = await db.Carts.Include(c => c.Items).SingleOrDefaultAsync(c => c.CustomerId == customerId, cancellationToken);
        if (cart is null) return EmptyCart();
        await db.LockCartAsync(cart.Id, cancellationToken);
        var item = cart.Items.SingleOrDefault(x => x.VariantId == variantId);
        if (item is not null) db.CartItems.Remove(item);
        cart.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(customerId, cancellationToken);
    }

    public async Task<CartDto> ClearAsync(string customerId, CancellationToken cancellationToken)
    {
        var cart = await db.Carts.Include(c => c.Items).SingleOrDefaultAsync(c => c.CustomerId == customerId, cancellationToken);
        if (cart is null) return EmptyCart();
        await db.LockCartAsync(cart.Id, cancellationToken);
        db.CartItems.RemoveRange(cart.Items);
        cart.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(customerId, cancellationToken);
    }

    private IQueryable<CartRow> QueryCart(string customerId) => db.Carts.AsNoTracking().Where(c => c.CustomerId == customerId).Select(c => new CartRow(c.Id, c.Version,
        c.Items.OrderBy(i => i.AddedAt).Select(i => new CartLineRow(i.VariantId, i.Variant.ProductId, i.Variant.Product.Slug, i.Variant.Product.Title, i.Variant.Name,
            i.Variant.Product.Assets.Where(a => a.Type == AssetType.PrimaryImage).OrderBy(a => a.SortOrder).Select(a => new ImageDto(a.Url, a.MimeType, a.Width, a.Height)).FirstOrDefault(),
            i.Quantity, i.Variant.Price, i.Variant.Currency, i.Variant.Inventory.QuantityOnHand)).ToList()));

    private static CartDto Map(CartRow row)
    {
        var items = row.Items.Select(i => new CartItemDto(i.VariantId, i.ProductId, i.Slug, i.Title, i.Variant, i.Image, i.Quantity, new MoneyDto(i.Price, i.Currency), new MoneyDto(i.Price * i.Quantity, i.Currency), i.Available >= i.Quantity ? "in_stock" : "insufficient_stock")).ToList();
        var currency = items.FirstOrDefault()?.UnitPrice.Currency ?? "USD";
        return new CartDto(row.Id, items.Sum(i => i.Quantity), new MoneyDto(items.Sum(i => i.LineTotal.Amount), currency), items, row.Version.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static CartDto EmptyCart() => new(Guid.Empty, 0, new MoneyDto(0, "USD"), [], "0");
    private sealed record CartRow(Guid Id, uint Version, IReadOnlyList<CartLineRow> Items);
    private sealed record CartLineRow(Guid VariantId, Guid ProductId, string Slug, string Title, string Variant, ImageDto? Image, int Quantity, decimal Price, string Currency, int Available);
}
