using Commerce.Domain;
using Microsoft.EntityFrameworkCore;

namespace Commerce.Infrastructure;

public static class CommerceSeeder
{
    public static async Task SeedAsync(CommerceDbContext db, CancellationToken cancellationToken)
    {
        if (await db.Products.AnyAsync(cancellationToken)) return;
        var electronics = new Category { Id = Id(1), Slug = "electronics", Name = "Electronics", SortOrder = 1 };
        var home = new Category { Id = Id(2), Slug = "home-kitchen", Name = "Home & Kitchen", SortOrder = 2 };
        var books = new Category { Id = Id(3), Slug = "books", Name = "Books", SortOrder = 3 };
        db.Categories.AddRange(electronics, home, books);
        var categories = new[] { electronics, home, books };
        var titles = new[] { "Noise-Cancelling Headphones", "Mechanical Keyboard", "Portable Speaker", "Smart Reading Lamp", "Pour-Over Coffee Set", "Cast Iron Skillet", "Platform Engineering Handbook", "Distributed Systems Field Guide", "Everyday Backpack", "USB-C Travel Hub", "Linen Sheet Set", "Digital Kitchen Scale", "Ergonomic Mouse", "Desk Organizer", "Wireless Charging Stand", "Insulated Water Bottle", "Compact Air Purifier", "E-Reader Cover", "Studio Microphone", "Adjustable Laptop Stand", "French Press", "Cookbook for Weeknights", "Cable Management Kit", "Webcam Light" };
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < titles.Length; i++)
        {
            var productId = Id(100 + i); var variantId = Id(1000 + i); var category = categories[i % categories.Length];
            var product = new Product { Id = productId, CategoryId = category.Id, Slug = Slug(titles[i]), Title = titles[i], Brand = i % 3 == 0 ? "Northstar" : i % 3 == 1 ? "Harbor" : "Foundry", Description = $"A reliable {titles[i].ToLowerInvariant()} designed for everyday use.", Status = ProductStatus.Active, IsFeatured = i < 12, CreatedAt = now.AddDays(-i), UpdatedAt = now };
            product.Variants.Add(new ProductVariant { Id = variantId, ProductId = productId, Sku = $"AM-{i + 1:0000}", Name = "Standard", Price = 19.99m + i * 7.25m, ListPrice = i % 4 == 0 ? 29.99m + i * 7.25m : null, Currency = "USD", IsActive = true, Inventory = new InventoryItem { VariantId = variantId, QuantityOnHand = 5 + i, UpdatedAt = now } });
            product.Assets.Add(new ProductAsset { Id = Id(2000 + i), ProductId = productId, Type = AssetType.PrimaryImage, Url = $"https://images.example.test/products/{product.Slug}.webp", MimeType = "image/webp", Width = 800, Height = 800, SortOrder = 0 });
            for (var ratingIndex = 0; ratingIndex < 3 + i % 5; ratingIndex++) product.Reviews.Add(new Review { Id = Id(3000 + i * 10 + ratingIndex), ProductId = productId, Rating = 3 + (i + ratingIndex) % 3, IsApproved = true });
            db.Products.Add(product);
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    private static Guid Id(int value) => Guid.Parse($"00000000-0000-0000-0000-{value:000000000000}");
    private static string Slug(string value) => value.ToLowerInvariant().Replace("-", " ").Replace(" ", "-");
}
