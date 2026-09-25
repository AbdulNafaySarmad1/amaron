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

        if (!await db.ProductRelationships.AnyAsync(cancellationToken))
        {
            var byTitle = await db.Products.Where(p => seedIds.Contains(p.Id)).ToDictionaryAsync(p => p.Title, p => p.Id, cancellationToken);
            foreach (var (source, target, type, score, reason) in SeedRelationships)
                if (byTitle.TryGetValue(source, out var sourceId) && byTitle.TryGetValue(target, out var targetId))
                    db.ProductRelationships.Add(new ProductRelationship { SourceProductId = sourceId, TargetProductId = targetId, Type = type, RelevanceScore = score, Reason = reason });
            await db.SaveChangesAsync(cancellationToken);
        }

        if (!await db.ProductTranslations.AnyAsync(cancellationToken) && !await db.CategoryTranslations.AnyAsync(cancellationToken))
        {
            foreach (var category in await db.Categories.Where(c => CommerceSeedTranslations.Categories.Keys.Contains(c.Slug)).ToListAsync(cancellationToken))
            {
                var (ru, ur, ar) = CommerceSeedTranslations.Categories[category.Slug];
                foreach (var (locale, name) in new[] { ("ru", ru), ("ur", ur), ("ar", ar) })
                    db.CategoryTranslations.Add(new CategoryTranslation { CategoryId = category.Id, Locale = locale, Name = name });
            }
            foreach (var product in await db.Products.Where(p => seedIds.Contains(p.Id)).ToListAsync(cancellationToken))
            {
                if (!CommerceSeedTranslations.Products.TryGetValue(product.Title, out var titles)) continue;
                var descriptions = CommerceSeedTranslations.Description(titles);
                foreach (var (locale, title, description) in new[] { ("ru", titles.Ru, descriptions.Ru), ("ur", titles.Ur, descriptions.Ur), ("ar", titles.Ar, descriptions.Ar) })
                    db.ProductTranslations.Add(new ProductTranslation { ProductId = product.Id, Locale = locale, Title = title, Description = description });
            }
            await db.SaveChangesAsync(cancellationToken);
        }

        var seedTime = DateTimeOffset.UtcNow;
        var warehouseId = Id(9000);
        if (!await db.Warehouses.AnyAsync(cancellationToken))
            db.Warehouses.Add(new Warehouse { Id = warehouseId, Code = "PRIMARY", Name = "Primary Warehouse", IsActive = true });
        if (!await db.PricingPolicies.AnyAsync(cancellationToken))
            db.PricingPolicies.Add(new PricingPolicy { Id = Id(9001), Currency = "USD", MinimumGrossMarginPercent = 10, MaximumChangePercent = 25, MaximumMarkdownPercent = 40, ApprovalThresholdPercent = 10, EnforceCostFloor = true, IsActive = true, UpdatedBy = "seed", UpdatedAt = seedTime });

        await db.SaveChangesAsync(cancellationToken);
        await EnsureOperationalRowsAsync(db, cancellationToken);
    }

    /// <summary>
    /// Every variant gets opening warehouse stock (with its ledger entry), an available balance and a price record.
    /// Set-based, so it costs one indexed anti-join per table on each startup however large the catalog is.
    /// </summary>
    public static async Task EnsureOperationalRowsAsync(CommerceDbContext db, CancellationToken cancellationToken)
    {
        var warehouseId = Id(9000);
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO inventory_ledger ("Id", "VariantId", "WarehouseId", "QuantityDelta", "Reason", "ReferenceType", "ReferenceId", "CreatedBy", "CreatedAt", "State")
            SELECT gen_random_uuid(), i."VariantId", {warehouseId}, i."QuantityOnHand", 'GoodsReceived', 'OpeningBalance', 'seed', 'seed', now(), 'Available'
            FROM inventory i WHERE NOT EXISTS (SELECT 1 FROM warehouse_stock s WHERE s."WarehouseId" = {warehouseId} AND s."VariantId" = i."VariantId")
            """, cancellationToken);
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO warehouse_stock ("WarehouseId", "VariantId", "OnHand", "Reserved", "SafetyStock", "Unavailable", "Inbound", "SupplierLeadTimeDays", "UpdatedAt")
            SELECT {warehouseId}, i."VariantId", i."QuantityOnHand", 0, 2, 0, 0, 7, now()
            FROM inventory i WHERE NOT EXISTS (SELECT 1 FROM warehouse_stock s WHERE s."WarehouseId" = {warehouseId} AND s."VariantId" = i."VariantId")
            """, cancellationToken);
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO inventory_balances ("Id", "WarehouseId", "VariantId", "State", "Quantity", "UpdatedAt")
            SELECT gen_random_uuid(), s."WarehouseId", s."VariantId", 'Available', s."OnHand", now()
            FROM warehouse_stock s WHERE s."WarehouseId" = {warehouseId}
              AND NOT EXISTS (SELECT 1 FROM inventory_balances b WHERE b."WarehouseId" = s."WarehouseId" AND b."VariantId" = s."VariantId" AND b."State" = 'Available')
            """, cancellationToken);
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO price_records ("Id", "VariantId", "Currency", "Price", "CompareAtPrice", "CostAtTime", "EffectiveFrom", "Reason", "CreatedBy", "CreatedAt", "ApprovedBy", "ApprovedAt", "Source", "Revision", "Kind", "Status")
            SELECT gen_random_uuid(), v."Id", v."Currency", v."Price", v."ListPrice", v."UnitCost", now(), 'Opening price history', 'seed', now(), 'seed', now(), 'Seed', 1, 'Regular', 'Applied'
            FROM product_variants v WHERE NOT EXISTS (SELECT 1 FROM price_records p WHERE p."VariantId" = v."Id")
            """, cancellationToken);
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

    private static readonly (string Source, string Target, RelationshipType Type, decimal Score, string Reason)[] SeedRelationships =
    [
        ("Pour-Over Coffee Set", "Digital Kitchen Scale", RelationshipType.Accessory, 0.95m, "Weigh beans and water for a consistent brew"),
        ("Pour-Over Coffee Set", "French Press", RelationshipType.Alternative, 0.70m, "Fuller-bodied coffee, no filters to buy"),
        ("French Press", "Digital Kitchen Scale", RelationshipType.Accessory, 0.85m, "Measure grounds the same way every morning"),
        ("French Press", "Pour-Over Coffee Set", RelationshipType.Alternative, 0.70m, "A cleaner, brighter cup"),
        ("Cast Iron Skillet", "Cookbook for Weeknights", RelationshipType.Complementary, 0.80m, "Most of its recipes use one pan"),
        ("Cookbook for Weeknights", "Cast Iron Skillet", RelationshipType.Complementary, 0.80m, "The pan the recipes are written around"),
        ("Adjustable Laptop Stand", "Mechanical Keyboard", RelationshipType.Complementary, 0.90m, "A raised screen needs a separate keyboard"),
        ("Adjustable Laptop Stand", "Ergonomic Mouse", RelationshipType.Complementary, 0.85m, "Completes a raised-screen setup"),
        ("Adjustable Laptop Stand", "USB-C Travel Hub", RelationshipType.Accessory, 0.75m, "One cable to the laptop, everything else through the hub"),
        ("Mechanical Keyboard", "Ergonomic Mouse", RelationshipType.Complementary, 0.80m, "Uses the same USB-C and Bluetooth connections"),
        ("Mechanical Keyboard", "Adjustable Laptop Stand", RelationshipType.Complementary, 0.70m, "Lift your screen to eye level"),
        ("Ergonomic Mouse", "Mechanical Keyboard", RelationshipType.Complementary, 0.70m, "Pairs over the same Bluetooth connection"),
        ("Studio Microphone", "Webcam Light", RelationshipType.Complementary, 0.85m, "Look as clear as you sound on calls"),
        ("Studio Microphone", "Noise-Cancelling Headphones", RelationshipType.Compatible, 0.80m, "Monitor your voice without echo"),
        ("Webcam Light", "Studio Microphone", RelationshipType.Complementary, 0.80m, "Sound as clear as you look"),
        ("Desk Organizer", "Cable Management Kit", RelationshipType.Complementary, 0.90m, "Keeps the cables behind it out of sight"),
        ("Cable Management Kit", "Desk Organizer", RelationshipType.Complementary, 0.80m, "A place for what the cables connect to"),
        ("E-Reader Cover", "Smart Reading Lamp", RelationshipType.Complementary, 0.70m, "Warm light for reading late"),
        ("Linen Sheet Set", "Smart Reading Lamp", RelationshipType.Complementary, 0.50m, "For bedside reading"),
        ("Noise-Cancelling Headphones", "Portable Speaker", RelationshipType.Alternative, 0.60m, "Music for the room instead of just you"),
        ("Portable Speaker", "Noise-Cancelling Headphones", RelationshipType.Alternative, 0.60m, "Private listening, with noise cancelling"),
        ("Distributed Systems Field Guide", "Platform Engineering Handbook", RelationshipType.Complementary, 0.85m, "Read next: running systems in production"),
        ("Platform Engineering Handbook", "Distributed Systems Field Guide", RelationshipType.Complementary, 0.85m, "The fundamentals under platform work"),
        ("Everyday Backpack", "Insulated Water Bottle", RelationshipType.Accessory, 0.75m, "Fits the side pocket"),
        ("Everyday Backpack", "USB-C Travel Hub", RelationshipType.Accessory, 0.60m, "Small enough for the front pocket"),
        ("Insulated Water Bottle", "Everyday Backpack", RelationshipType.Complementary, 0.50m, "Has a side pocket sized for it"),
    ];

    private static Guid Id(int value) => Guid.Parse($"00000000-0000-0000-0000-{value:000000000000}");
    private static string Slug(string value) => value.ToLowerInvariant().Replace("-", " ").Replace(" ", "-");
}
