using Commerce.Contracts;
using Commerce.Domain;
using Microsoft.EntityFrameworkCore;

namespace Commerce.Application;

public sealed class AdminCatalogService(ICommerceDbContext db, IReadModelCache cache, TimeProvider clock)
{
    public async Task<AdminProductDto> UpdateProductAsync(Guid productId, uint expectedVersion, UpdateProductRequest request, CancellationToken cancellationToken)
    {
        Validate(request);
        var product = await db.Products.SingleOrDefaultAsync(x => x.Id == productId, cancellationToken)
            ?? throw CommerceErrors.NotFound("Product");
        if (product.Version != expectedVersion) throw CommerceErrors.Conflict("concurrency_conflict", "The product changed since it was loaded. Refresh it and try again.");
        if (!Enum.TryParse<ProductStatus>(request.Status, true, out var status)) throw CommerceErrors.Validation("Status must be Draft, Active, or Archived.");

        var oldSlug = product.Slug;
        product.Title = request.Title.Trim();
        product.Brand = request.Brand.Trim();
        product.Description = request.Description.Trim();
        product.IsFeatured = request.IsFeatured;
        product.Status = status;
        product.UpdatedAt = clock.GetUtcNow();
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { throw CommerceErrors.Conflict("concurrency_conflict", "The product changed while it was being updated. Refresh it and try again."); }

        await InvalidateProductAsync(product.Id, oldSlug, CancellationToken.None);
        return Map(product);
    }

    public async Task<InventoryDto> UpdateInventoryAsync(Guid variantId, uint expectedVersion, UpdateInventoryRequest request, CancellationToken cancellationToken)
    {
        if (request.QuantityOnHand is < 0 or > 1_000_000) throw CommerceErrors.Validation("Quantity on hand must be between 0 and 1,000,000.");
        var inventory = await db.Inventory.Include(x => x.Variant).ThenInclude(x => x.Product).SingleOrDefaultAsync(x => x.VariantId == variantId, cancellationToken)
            ?? throw CommerceErrors.NotFound("Inventory item");
        if (inventory.Version != expectedVersion) throw CommerceErrors.Conflict("concurrency_conflict", "Inventory changed since it was loaded. Refresh it and try again.");

        inventory.QuantityOnHand = request.QuantityOnHand;
        inventory.UpdatedAt = clock.GetUtcNow();
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { throw CommerceErrors.Conflict("concurrency_conflict", "Inventory changed while it was being updated. Refresh it and try again."); }

        await cache.RemoveByTagAsync($"inventory:{variantId}", CancellationToken.None);
        await InvalidateProductAsync(inventory.Variant.ProductId, inventory.Variant.Product.Slug, CancellationToken.None);
        return new InventoryDto(inventory.VariantId, inventory.QuantityOnHand, inventory.Version.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    public async Task<AdminProductDto> GetProductAsync(Guid productId, CancellationToken cancellationToken)
    {
        var product = await db.Products.AsNoTracking().SingleOrDefaultAsync(x => x.Id == productId, cancellationToken)
            ?? throw CommerceErrors.NotFound("Product");
        return Map(product);
    }

    public async Task<InventoryDto> GetInventoryAsync(Guid variantId, CancellationToken cancellationToken)
    {
        var inventory = await db.Inventory.AsNoTracking().SingleOrDefaultAsync(x => x.VariantId == variantId, cancellationToken)
            ?? throw CommerceErrors.NotFound("Inventory item");
        return new InventoryDto(inventory.VariantId, inventory.QuantityOnHand, inventory.Version.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private async Task InvalidateProductAsync(Guid productId, string slug, CancellationToken cancellationToken)
    {
        foreach (var tag in new[] { "products", "homepage", "search-suggestions", $"product:{productId}", $"product:{slug.ToLowerInvariant()}" })
            await cache.RemoveByTagAsync(tag, cancellationToken);
    }

    private static AdminProductDto Map(Product product) => new(product.Id, product.Slug, product.Title, product.Brand, product.Description, product.Status.ToString(), product.IsFeatured, product.Version.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private static void Validate(UpdateProductRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Length > 240) throw CommerceErrors.Validation("Title is required and cannot exceed 240 characters.");
        if (string.IsNullOrWhiteSpace(request.Brand) || request.Brand.Length > 120) throw CommerceErrors.Validation("Brand is required and cannot exceed 120 characters.");
        if (request.Description is null || request.Description.Length > 8000) throw CommerceErrors.Validation("Description is required and cannot exceed 8,000 characters.");
    }
}
