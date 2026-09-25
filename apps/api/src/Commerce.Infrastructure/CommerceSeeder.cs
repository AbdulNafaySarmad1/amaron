using Commerce.Domain;
using Microsoft.EntityFrameworkCore;

namespace Commerce.Infrastructure;

public static class CommerceSeeder
{
    public static async Task SeedAsync(CommerceDbContext db, CancellationToken cancellationToken)
    {
        if (!await db.Products.AnyAsync(cancellationToken))
        {
        var electronics = new Category { Id = Id(1), Slug = "electronics", Name = "Electronics", SortOrder = 1 };
        var home = new Category { Id = Id(2), Slug = "home-kitchen", Name = "Home & Kitchen", SortOrder = 2 };
        var books = new Category { Id = Id(3), Slug = "books", Name = "Books", SortOrder = 3 };
        db.Categories.AddRange(electronics, home, books);
        // Each title names its category explicitly. Product IDs are positional (Id(100 + i)); migration
        // FixSeedProductCategories repairs databases seeded by the old round-robin assignment.
        var catalog = SeedCatalog(electronics, home, books);
        var titles = catalog.Select(x => x.Title).ToArray();
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < titles.Length; i++)
        {
            var productId = Id(100 + i); var variantId = Id(1000 + i); var category = catalog[i].Category;
            var product = new Product { Id = productId, CategoryId = category.Id, Slug = Slug(titles[i]), Title = titles[i], Brand = i % 3 == 0 ? "Northstar" : i % 3 == 1 ? "Harbor" : "Foundry", Description = $"A reliable {titles[i].ToLowerInvariant()} designed for everyday use.", Status = ProductStatus.Active, IsFeatured = i < 12, CreatedAt = now.AddDays(-i), UpdatedAt = now };
            var price = 19.99m + i * 7.25m;
            product.Variants.Add(new ProductVariant { Id = variantId, ProductId = productId, Sku = $"AM-{i + 1:0000}", Name = "Standard", Price = price, ListPrice = i % 4 == 0 ? 29.99m + i * 7.25m : null, UnitCost = decimal.Round(price * .58m, 2), Currency = "USD", IsActive = true, Inventory = new InventoryItem { VariantId = variantId, QuantityOnHand = 5 + i, UpdatedAt = now } });
            ApplyDetails(product);
            product.Assets.Add(new ProductAsset { Id = Id(2000 + i), ProductId = productId, Type = AssetType.PrimaryImage, Url = $"https://images.example.test/products/{product.Slug}.webp", MimeType = "image/webp", Width = 800, Height = 800, SortOrder = 0 });
            for (var ratingIndex = 0; ratingIndex < 3 + i % 5; ratingIndex++) product.Reviews.Add(new Review { Id = Id(3000 + i * 10 + ratingIndex), ProductId = productId, Rating = 3 + (i + ratingIndex) % 3, IsApproved = true });
            db.Products.Add(product);
        }
        await db.SaveChangesAsync(cancellationToken);
        }

        // Databases seeded before products had details: fill them in, but never overwrite details someone has set.
        var seedIds = Enumerable.Range(0, SeedDetails.Count).Select(i => Id(100 + i)).ToList();
        var undetailed = await db.Products.Where(p => seedIds.Contains(p.Id) && p.Kind == "general").ToListAsync(cancellationToken);
        foreach (var product in undetailed.Where(p => p.Attributes.Count == 0)) ApplyDetails(product);
        if (undetailed.Count > 0) await db.SaveChangesAsync(cancellationToken);

        var seedTime = DateTimeOffset.UtcNow;
        var warehouseId = Id(9000);
        if (!await db.Warehouses.AnyAsync(cancellationToken))
            db.Warehouses.Add(new Warehouse { Id = warehouseId, Code = "PRIMARY", Name = "Primary Warehouse", IsActive = true });
        if (!await db.PricingPolicies.AnyAsync(cancellationToken))
            db.PricingPolicies.Add(new PricingPolicy { Id = Id(9001), Currency = "USD", MinimumGrossMarginPercent = 10, MaximumChangePercent = 25, MaximumMarkdownPercent = 40, ApprovalThresholdPercent = 10, EnforceCostFloor = true, IsActive = true, UpdatedBy = "seed", UpdatedAt = seedTime });

        var variants = await db.ProductVariants.Include(x => x.Inventory).ToListAsync(cancellationToken);
        var warehouseStocks = await db.WarehouseStocks.Where(x => x.WarehouseId == warehouseId).ToDictionaryAsync(x => x.VariantId, cancellationToken);
        var balanced = await db.InventoryBalances.Where(x => x.WarehouseId == warehouseId && x.State == InventoryState.Available).Select(x => x.VariantId).Distinct().ToListAsync(cancellationToken);
        var priced = await db.PriceRecords.Select(x => x.VariantId).ToListAsync(cancellationToken);
        foreach (var variant in variants)
        {
            if (!warehouseStocks.TryGetValue(variant.Id, out var stock))
            {
                stock = new WarehouseStock { WarehouseId = warehouseId, VariantId = variant.Id, OnHand = variant.Inventory.QuantityOnHand, SafetyStock = 2, SupplierLeadTimeDays = 7, UpdatedAt = seedTime };
                db.WarehouseStocks.Add(stock);
                db.InventoryLedgerEntries.Add(new InventoryLedgerEntry { Id = Guid.CreateVersion7(), VariantId = variant.Id, WarehouseId = warehouseId, QuantityDelta = variant.Inventory.QuantityOnHand, Reason = InventoryMovementReason.GoodsReceived, ReferenceType = "OpeningBalance", ReferenceId = "seed", CreatedBy = "seed", CreatedAt = seedTime });
            }
            if (!balanced.Contains(variant.Id))
                db.InventoryBalances.Add(new InventoryBalance { Id = Guid.CreateVersion7(), WarehouseId = warehouseId, VariantId = variant.Id, State = InventoryState.Available, Quantity = stock.OnHand, UpdatedAt = seedTime });
            if (!priced.Contains(variant.Id))
                db.PriceRecords.Add(new PriceRecord { Id = Guid.CreateVersion7(), VariantId = variant.Id, Currency = variant.Currency, Price = variant.Price, CompareAtPrice = variant.ListPrice, CostAtTime = variant.UnitCost, EffectiveFrom = seedTime, Reason = "Opening price history", CreatedBy = "seed", CreatedAt = seedTime, ApprovedBy = "seed", ApprovedAt = seedTime, Source = "Seed", Revision = 1, Kind = PriceKind.Regular, Status = OperationalStatus.Applied });
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    private static (string Title, Category Category)[] SeedCatalog(Category electronics, Category home, Category books) =>
    [
        ("Noise-Cancelling Headphones", electronics), ("Mechanical Keyboard", electronics), ("Portable Speaker", electronics), ("Smart Reading Lamp", home),
        ("Pour-Over Coffee Set", home), ("Cast Iron Skillet", home), ("Platform Engineering Handbook", books), ("Distributed Systems Field Guide", books),
        ("Everyday Backpack", home), ("USB-C Travel Hub", electronics), ("Linen Sheet Set", home), ("Digital Kitchen Scale", home),
        ("Ergonomic Mouse", electronics), ("Desk Organizer", home), ("Wireless Charging Stand", electronics), ("Insulated Water Bottle", home),
        ("Compact Air Purifier", home), ("E-Reader Cover", electronics), ("Studio Microphone", electronics), ("Adjustable Laptop Stand", electronics),
        ("French Press", home), ("Cookbook for Weeknights", books), ("Cable Management Kit", electronics), ("Webcam Light", electronics),
    ];

private static void ApplyDetails(Product product)
    {
        if (!SeedDetails.TryGetValue(product.Title, out var details)) return;
        product.Kind = details.Kind;
        product.Attributes = details.Attributes.Select(a => new ProductAttribute { Label = a.Label.TrimStart('*'), Value = a.Value, Highlight = a.Label.StartsWith('*') }).ToList();
    }

    // "*" marks the attributes a product card highlights. ISBNs use the 979-8-88888 block reserved here for synthetic data.
    private static readonly Dictionary<string, (string Kind, (string Label, string Value)[] Attributes)> SeedDetails = new()
    {
        ["Noise-Cancelling Headphones"] = ("headphones", [("*Type", "Over-ear"), ("*Noise cancelling", "Adaptive ANC"), ("*Battery", "30 hours"), ("Connectivity", "Bluetooth 5.3"), ("Weight", "250 g")]),
        ["Mechanical Keyboard"] = ("keyboard", [("*Layout", "75% ANSI"), ("*Switches", "Linear, hot-swappable"), ("*Connection", "USB-C, Bluetooth"), ("Backlight", "White LED")]),
        ["Portable Speaker"] = ("speaker", [("*Battery", "16 hours"), ("*Water resistance", "IP67"), ("*Output", "20 W"), ("Connectivity", "Bluetooth 5.3")]),
        ["Smart Reading Lamp"] = ("lamp", [("*Colour temperature", "2700–6500 K"), ("*Brightness", "800 lm"), ("*Power", "USB-C"), ("Control", "Touch and app")]),
        ["Pour-Over Coffee Set"] = ("coffee", [("*Capacity", "600 ml"), ("*Material", "Borosilicate glass"), ("*Includes", "Dripper, carafe, 40 filters")]),
        ["Cast Iron Skillet"] = ("cookware", [("*Diameter", "26 cm"), ("*Hobs", "All, including induction"), ("*Finish", "Pre-seasoned"), ("Weight", "2.4 kg")]),
        ["Platform Engineering Handbook"] = ("book", [("*Author", "M. Okafor"), ("*Format", "Paperback"), ("*Pages", "412"), ("Language", "English"), ("Publisher", "Harbor Press"), ("ISBN", "979-8-88888-001-4")]),
        ["Distributed Systems Field Guide"] = ("book", [("*Author", "L. Brennan"), ("*Format", "Hardcover"), ("*Pages", "528"), ("Language", "English"), ("Publisher", "Foundry Books"), ("ISBN", "979-8-88888-002-1")]),
        ["Everyday Backpack"] = ("bag", [("*Capacity", "22 L"), ("*Laptop sleeve", "Up to 16 in"), ("*Material", "Recycled nylon")]),
        ["USB-C Travel Hub"] = ("hub", [("*Ports", "7 in 1"), ("*Power delivery", "100 W pass-through"), ("*Video", "4K 60 Hz HDMI")]),
        ["Linen Sheet Set"] = ("bedding", [("*Size", "Queen"), ("*Material", "European flax linen"), ("*Includes", "Flat and fitted sheet, 2 pillowcases")]),
        ["Digital Kitchen Scale"] = ("scale", [("*Capacity", "5 kg"), ("*Precision", "1 g"), ("*Power", "USB-C rechargeable")]),
        ["Ergonomic Mouse"] = ("mouse", [("*Grip", "Vertical, right-handed"), ("*Sensor", "4,000 DPI"), ("*Connection", "Bluetooth, 2.4 GHz")]),
        ["Desk Organizer"] = ("storage", [("*Size", "40 × 18 × 9 cm"), ("*Material", "Oak veneer"), ("*Compartments", "5")]),
        ["Wireless Charging Stand"] = ("charger", [("*Output", "15 W Qi2"), ("*Charges", "Phone and earbuds"), ("*Cable", "USB-C, 1.5 m")]),
        ["Insulated Water Bottle"] = ("drinkware", [("*Capacity", "750 ml"), ("*Keeps cold", "24 hours"), ("*Material", "Stainless steel")]),
        ["Compact Air Purifier"] = ("appliance", [("*Room size", "Up to 30 m²"), ("*Filter", "H13 HEPA"), ("*Noise", "24 dB in sleep mode")]),
        ["E-Reader Cover"] = ("accessory", [("*Fits", "6.8 in e-readers"), ("*Material", "Vegan leather"), ("*Feature", "Auto sleep and wake")]),
        ["Studio Microphone"] = ("microphone", [("*Pattern", "Cardioid condenser"), ("*Connection", "USB-C, XLR"), ("*Resolution", "24-bit / 96 kHz")]),
        ["Adjustable Laptop Stand"] = ("stand", [("*Fits", "11–17 in laptops"), ("*Heights", "6 levels"), ("*Material", "Aluminium")]),
        ["French Press"] = ("coffee", [("*Capacity", "1 l"), ("*Material", "Double-wall stainless steel"), ("*Serves", "4 cups")]),
        ["Cookbook for Weeknights"] = ("book", [("*Author", "R. Haddad"), ("*Format", "Hardcover"), ("*Pages", "256"), ("Language", "English"), ("Publisher", "Northstar Kitchen"), ("ISBN", "979-8-88888-003-8")]),
        ["Cable Management Kit"] = ("accessory", [("*Includes", "40 pieces"), ("*Material", "Silicone and hook-and-loop"), ("*Colours", "Graphite, sand")]),
        ["Webcam Light"] = ("light", [("*Brightness", "10 levels"), ("*Colour temperature", "3200–5600 K"), ("*Mount", "Clip-on")]),
    };

    private static Guid Id(int value) => Guid.Parse($"00000000-0000-0000-0000-{value:000000000000}");
    private static string Slug(string value) => value.ToLowerInvariant().Replace("-", " ").Replace(" ", "-");
}
