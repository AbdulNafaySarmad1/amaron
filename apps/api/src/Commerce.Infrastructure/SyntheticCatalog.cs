using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Commerce.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Commerce.Infrastructure;

/// <summary>
/// Deterministic synthetic catalog for scale and relevance testing (development only, SYNTHETIC_CATALOG_PRODUCTS):
/// 25 departments of real product families, each product carrying the attributes its type actually has.
/// Generation is pure and seeded, so the same size always yields the same catalog; rows are bulk-copied in one transaction.
/// </summary>
public static class SyntheticCatalog
{
    public sealed record Department(string Slug, string Name, string Translations, string Use, string[] Brands, Subcategory[] Subcategories);
    public sealed record Subcategory(string Slug, string Name, string Translations, string Kind, Noun[] Nouns, decimal MinPrice, decimal MaxPrice, Attr[] Attributes, string[]? TitleForms = null);
    public sealed record Attr(string Label, string[] Values);
    /// <summary>A kind of product within a subcategory. It may pin attribute values (earbuds are in-ear) and its own price range.</summary>
    public sealed record Noun(string Name, decimal? MinPrice = null, decimal? MaxPrice = null, IReadOnlyDictionary<string, string[]>? Fixed = null);
    public sealed record GeneratedProduct(int Index, string CategorySlug, string Slug, string Title, string Brand, string Description, string Kind,
        List<ProductAttribute> Attributes, string Sku, decimal Price, decimal? ListPrice, decimal UnitCost, int OnHand, int[] Ratings, DateTimeOffset CreatedAt);

    /// <summary>Loads the catalog once (marker: the first synthetic department exists). Returns whether anything was loaded.</summary>
    public static async Task<bool> LoadAsync(CommerceDbContext db, int total, CancellationToken cancellationToken)
    {
        if (total <= 0 || await db.Categories.AnyAsync(c => c.Slug == Departments[0].Slug, cancellationToken)) return false;
        db.Database.SetCommandTimeout(TimeSpan.FromMinutes(15));
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var categoryIds = await AddCategoriesAsync(db, cancellationToken);

        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open) await connection.OpenAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        await CopyAsync(connection, """COPY products ("Id","CategoryId","Slug","Title","Brand","Description","Status","IsFeatured","CreatedAt","UpdatedAt","Kind",attributes) FROM STDIN (FORMAT BINARY)""", total, now, async (w, p) =>
        {
            await w.WriteAsync(ProductId(p.Index), NpgsqlDbType.Uuid); await w.WriteAsync(categoryIds[p.CategorySlug], NpgsqlDbType.Uuid);
            await w.WriteAsync(p.Slug, NpgsqlDbType.Varchar); await w.WriteAsync(p.Title, NpgsqlDbType.Varchar); await w.WriteAsync(p.Brand, NpgsqlDbType.Varchar);
            await w.WriteAsync(p.Description, NpgsqlDbType.Varchar); await w.WriteAsync(nameof(ProductStatus.Active), NpgsqlDbType.Varchar); await w.WriteAsync(false, NpgsqlDbType.Boolean);
            await w.WriteAsync(p.CreatedAt, NpgsqlDbType.TimestampTz); await w.WriteAsync(now, NpgsqlDbType.TimestampTz); await w.WriteAsync(p.Kind, NpgsqlDbType.Varchar);
            await w.WriteAsync(JsonSerializer.Serialize(p.Attributes), NpgsqlDbType.Jsonb);
        });
        await CopyAsync(connection, """COPY product_variants ("Id","ProductId","Sku","Name","Price","ListPrice","Currency","IsActive","UnitCost") FROM STDIN (FORMAT BINARY)""", total, now, async (w, p) =>
        {
            await w.WriteAsync(VariantId(p.Index), NpgsqlDbType.Uuid); await w.WriteAsync(ProductId(p.Index), NpgsqlDbType.Uuid); await w.WriteAsync(p.Sku, NpgsqlDbType.Varchar);
            await w.WriteAsync("Standard", NpgsqlDbType.Varchar); await w.WriteAsync(p.Price, NpgsqlDbType.Numeric);
            if (p.ListPrice is { } list) await w.WriteAsync(list, NpgsqlDbType.Numeric); else await w.WriteNullAsync();
            await w.WriteAsync("USD", NpgsqlDbType.Char); await w.WriteAsync(true, NpgsqlDbType.Boolean); await w.WriteAsync(p.UnitCost, NpgsqlDbType.Numeric);
        });
        await CopyAsync(connection, """COPY inventory ("VariantId","QuantityOnHand","UpdatedAt") FROM STDIN (FORMAT BINARY)""", total, now, async (w, p) =>
        {
            await w.WriteAsync(VariantId(p.Index), NpgsqlDbType.Uuid); await w.WriteAsync(p.OnHand, NpgsqlDbType.Integer); await w.WriteAsync(now, NpgsqlDbType.TimestampTz);
        });
        await CopyAsync(connection, """COPY product_assets ("Id","ProductId","Type","Url","MimeType","Width","Height","SortOrder") FROM STDIN (FORMAT BINARY)""", total, now, async (w, p) =>
        {
            await w.WriteAsync(AssetId(p.Index), NpgsqlDbType.Uuid); await w.WriteAsync(ProductId(p.Index), NpgsqlDbType.Uuid); await w.WriteAsync(nameof(AssetType.PrimaryImage), NpgsqlDbType.Varchar);
            await w.WriteAsync($"https://images.example.test/products/{p.Slug}.webp", NpgsqlDbType.Varchar); await w.WriteAsync("image/webp", NpgsqlDbType.Varchar);
            await w.WriteAsync(800, NpgsqlDbType.Integer); await w.WriteAsync(800, NpgsqlDbType.Integer); await w.WriteAsync(0, NpgsqlDbType.Integer);
        });
        await using (var reviews = await connection.BeginBinaryImportAsync("""COPY reviews ("Id","ProductId","Rating","IsApproved") FROM STDIN (FORMAT BINARY)""", cancellationToken))
        {
            reviews.Timeout = TimeSpan.FromMinutes(15);
            foreach (var p in Generate(total, now))
                for (var k = 0; k < p.Ratings.Length; k++)
                {
                    await reviews.StartRowAsync(cancellationToken);
                    await reviews.WriteAsync(ReviewId(p.Index, k), NpgsqlDbType.Uuid, cancellationToken); await reviews.WriteAsync(ProductId(p.Index), NpgsqlDbType.Uuid, cancellationToken);
                    await reviews.WriteAsync(p.Ratings[k], NpgsqlDbType.Integer, cancellationToken); await reviews.WriteAsync(true, NpgsqlDbType.Boolean, cancellationToken);
                }
            await reviews.CompleteAsync(cancellationToken);
        }

        await CommerceSeeder.EnsureOperationalRowsAsync(db, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        // Outside the transaction: fresh statistics, and a visibility map so index-only scans skip the heap.
        await db.Database.ExecuteSqlRawAsync("VACUUM ANALYZE products, product_variants, inventory, product_assets, reviews, warehouse_stock, inventory_balances, inventory_ledger, price_records, categories", cancellationToken);
        return true;
    }

    /// <summary>Pure and deterministic: departments get equal shares, round-robin across their subcategories.</summary>
    public static IEnumerable<GeneratedProduct> Generate(int total, DateTimeOffset now)
    {
        var index = 0;
        for (var d = 0; d < Departments.Length; d++)
        {
            var department = Departments[d];
            var count = total / Departments.Length + (d < total % Departments.Length ? 1 : 0);
            for (var k = 0; k < count; k++, index++)
                yield return Make(index, department, department.Subcategories[k % department.Subcategories.Length], now);
        }
    }

    private static GeneratedProduct Make(int index, Department department, Subcategory sub, DateTimeOffset now)
    {
        var rng = new Random(index * 7919 + 17);
        T Pick<T>(IReadOnlyList<T> values) => values[rng.Next(values.Count)];
        var brand = Pick(department.Brands);
        var noun = Pick(sub.Nouns);
        string title, description;
        List<ProductAttribute> attributes;
        if (sub.TitleForms is not null)
        {
            // Books: brand is the publisher; title, author, format and ISBN are generated.
            title = $"{noun.Name} {Pick(sub.TitleForms)}";
            var author = $"{Pick(Initials)}. {Pick(Surnames)}";
            var format = Pick(sub.Attributes[0].Values);
            var pages = 120 + rng.Next(60) * 10;
            var language = rng.NextDouble() < 0.85 ? "English" : Pick(new[] { "Russian", "Arabic", "Urdu" });
            attributes =
            [
                new() { Label = "Author", Value = author, Highlight = true },
                new() { Label = "Format", Value = format, Highlight = true },
                new() { Label = "Pages", Value = pages.ToString(CultureInfo.InvariantCulture), Highlight = true },
                new() { Label = "Language", Value = language },
                new() { Label = "Publisher", Value = brand },
                new() { Label = "ISBN", Value = Isbn(index) },
            ];
            description = $"{title} by {author}. A {format.ToLowerInvariant()} {sub.Name.ToLowerInvariant()} title from {brand}, {pages} pages.";
        }
        else
        {
            var model = $"{Pick(Series)}{Pick(Models)}";
            // The first three attributes are the ones a shopper decides on: highlighted on cards and offered as filters.
            attributes = sub.Attributes.Select((a, i) => new ProductAttribute
            {
                Label = a.Label, Value = Pick(noun.Fixed?.GetValueOrDefault(a.Label) ?? a.Values), Highlight = i < 3,
            }).ToList();
            title = $"{brand} {model} {noun.Name}, {attributes[0].Value}";
            description = $"The {brand} {model} {noun.Name.ToLowerInvariant()}, made for {department.Use}. " + string.Join(" ", attributes.Select(a => $"{a.Label}: {a.Value}."));
        }

        var (min, max) = (noun.MinPrice ?? sub.MinPrice, noun.MaxPrice ?? sub.MaxPrice);
        var price = Price(min * (decimal)Math.Pow((double)(max / min), rng.NextDouble()));
        decimal? listPrice = rng.NextDouble() < 0.22 ? Price(price * (1.1m + (decimal)rng.NextDouble() * 0.25m)) : null;
        if (listPrice <= price) listPrice = null;
        var stockRoll = rng.NextDouble();
        // Seeded safety stock is 2, so on-hand 0-2 sells nothing and 3-12 shows as low stock.
        var onHand = stockRoll < 0.06 ? rng.Next(3) : stockRoll < 0.2 ? 3 + rng.Next(10) : 13 + rng.Next(240);
        var quality = 3.2 + rng.NextDouble() * 1.6;
        var ratings = rng.NextDouble() < 0.3 ? [] : Enumerable.Range(0, 1 + rng.Next(15)).Select(_ => Math.Clamp((int)Math.Round(quality + (rng.NextDouble() - 0.5) * 2.4), 1, 5)).ToArray();
        return new GeneratedProduct(index, sub.Slug, $"{Slugify(title)}-{index:x}", title, brand, description, sub.Kind, attributes, $"SY-{index:000000}",
            price, listPrice, decimal.Round(price * (0.52m + (decimal)rng.NextDouble() * 0.18m), 2), onHand, ratings, now.AddDays(-rng.Next(720)).AddMinutes(-rng.Next(1440)));
    }

    private static async Task CopyAsync(NpgsqlConnection connection, string copy, int total, DateTimeOffset now, Func<NpgsqlBinaryImporter, GeneratedProduct, Task> write)
    {
        await using var importer = await connection.BeginBinaryImportAsync(copy);
        importer.Timeout = TimeSpan.FromMinutes(15);
        foreach (var product in Generate(total, now)) { await importer.StartRowAsync(); await write(importer, product); }
        await importer.CompleteAsync();
    }

    private static async Task<Dictionary<string, Guid>> AddCategoriesAsync(CommerceDbContext db, CancellationToken cancellationToken)
    {
        var existing = await db.Categories.ToDictionaryAsync(c => c.Slug, cancellationToken);
        var sort = existing.Count == 0 ? 0 : existing.Values.Max(c => c.SortOrder);
        var ids = new Dictionary<string, Guid>();
        void Translate(Guid id, string translations)
        {
            var names = translations.Split('|');
            foreach (var (locale, name) in new[] { ("ru", names[0]), ("ur", names[1]), ("ar", names[2]) })
                db.CategoryTranslations.Add(new CategoryTranslation { CategoryId = id, Locale = locale, Name = name });
        }
        foreach (var department in Departments)
        {
            // electronics, home-kitchen and books already exist (with translations): products nest under them.
            if (!existing.TryGetValue(department.Slug, out var parent))
            {
                parent = new Category { Id = Guid.CreateVersion7(), Slug = department.Slug, Name = department.Name, SortOrder = ++sort };
                db.Categories.Add(parent);
                Translate(parent.Id, department.Translations);
            }
            ids[department.Slug] = parent.Id;
            for (var s = 0; s < department.Subcategories.Length; s++)
            {
                var sub = department.Subcategories[s];
                var category = new Category { Id = Guid.CreateVersion7(), ParentId = parent.Id, Slug = sub.Slug, Name = sub.Name, SortOrder = s + 1 };
                db.Categories.Add(category);
                Translate(category.Id, sub.Translations);
                ids[sub.Slug] = category.Id;
            }
        }
        await db.SaveChangesAsync(cancellationToken);
        return ids;
    }

    public static Guid ProductId(int index) => Guid.Parse($"00000000-0000-0001-0000-{index:000000000000}");
    private static Guid VariantId(int index) => Guid.Parse($"00000000-0000-0002-0000-{index:000000000000}");
    private static Guid AssetId(int index) => Guid.Parse($"00000000-0000-0003-0000-{index:000000000000}");
    private static Guid ReviewId(int index, int k) => Guid.Parse($"00000000-0000-0004-{k:0000}-{index:000000000000}");

    private static decimal Price(decimal value) => value < 10 ? decimal.Floor(value) + 0.49m : decimal.Floor(value) + 0.99m;

    public static string Slugify(string value)
    {
        var slug = new StringBuilder();
        foreach (var c in value.ToLowerInvariant())
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9') slug.Append(c);
            else if (slug.Length > 0 && slug[^1] != '-') slug.Append('-');
        var result = slug.ToString().Trim('-');
        return result.Length > 120 ? result[..120].TrimEnd('-') : result;
    }

    /// <summary>ISBN-13 in the 979-8 range with a valid check digit; synthetic, not registered to any publisher.</summary>
    public static string Isbn(int index)
    {
        var body = $"9798{80_000_000 + index:00000000}";
        var sum = body.Select((c, i) => (c - '0') * (i % 2 == 0 ? 1 : 3)).Sum();
        return $"979-8-{body[4..]}-{(10 - sum % 10) % 10}";
    }

    private static readonly string[] Series = ["Aero", "Vela", "Arc", "Nova", "Terra", "Pulse", "Atlas", "Orbit", "Summit", "Crest", "Halo", "Nimbus", "Vista", "Echo", "Drift", "Forge", "Ember", "Tide", "Quill", "Solis"];
    private static readonly string[] Models = ["", " 2", " 3", " 5", " Pro", " Mini", " Max", " Plus", " S", " Lite"];
    private static readonly string[] Initials = ["A", "B", "C", "D", "E", "F", "H", "I", "J", "K", "L", "M", "N", "O", "P", "R", "S", "T", "Y", "Z"];
    private static readonly string[] Surnames = ["Okafor", "Brennan", "Haddad", "Ivanova", "Qureshi", "Lindqvist", "Moreau", "Tanaka", "Mensah", "Castillo", "Novak", "Farooq", "Adeyemi", "Kowalski", "Rahman", "Sørensen", "Petrov", "Nakamura", "Oyelaran", "Whitfield", "Al-Masri", "Chaudhry", "Delacroix", "Evans"];

    private static Attr A(string label, string values) => new(label, values.Split('|'));
    /// <summary>"Label=a~b" pins that attribute to a or b for this noun.</summary>
    private static Noun N(string name, params string[] pinned) => new(name, null, null, Pins(pinned));
    private static Noun N(string name, decimal min, decimal max, params string[] pinned) => new(name, min, max, Pins(pinned));
    private static Noun[] Ns(string names) => names.Split('|').Select(n => new Noun(n)).ToArray();
    private static Dictionary<string, string[]>? Pins(string[] pinned) =>
        pinned.Length == 0 ? null : pinned.Select(p => p.Split('=', 2)).ToDictionary(p => p[0], p => p[1].Split('~'));
    private static Subcategory S(string slug, string name, string translations, string kind, Noun[] nouns, decimal min, decimal max, params Attr[] attributes) =>
        new(slug, name, translations, kind, nouns, min, max, attributes);
    private static Subcategory Books(string slug, string name, string translations, string topics, string forms) =>
        new(slug, name, translations, "book", Ns(topics), 7.99m, 59.99m, [A("Format", "Paperback|Paperback|Hardcover|E-book|Audiobook")], forms.Split('|'));

    private const string Wireless = "Bluetooth~2.4 GHz and Bluetooth";

    public static readonly Department[] Departments =
    [
        new("computing", "Computing", "Компьютеры|کمپیوٹنگ|الحوسبة", "work, study and play", ["Kestrel", "Vantor", "Arclight", "Helio", "Brightline"],
        [
            S("laptops", "Laptops", "Ноутбуки|لیپ ٹاپ|أجهزة لابتوب", "laptop",
                [N("Laptop"), N("Ultrabook", 899, 2499, "Screen size=13.3 in~14 in", "GPU=Integrated", "Weight=1.1 kg~1.3 kg"), N("Gaming Laptop", 999, 2899, "Screen size=15.6 in~16 in", "GPU=8 GB discrete~12 GB discrete", "Weight=2.1 kg~2.4 kg"), N("2-in-1 Laptop", "Screen size=13.3 in~14 in", "Weight=1.3 kg~1.6 kg")],
                449, 2299,
                A("Screen size", "13.3 in|14 in|15.6 in|16 in"), A("RAM", "8 GB|16 GB|32 GB|64 GB"), A("Storage", "256 GB SSD|512 GB SSD|1 TB SSD|2 TB SSD"),
                A("CPU", "6-core, 4.4 GHz|8-core, 4.8 GHz|10-core, 5.0 GHz|12-core, 5.1 GHz|14-core, 5.4 GHz"), A("GPU", "Integrated|6 GB discrete|8 GB discrete"),
                A("Display", "1920×1080 IPS|1920×1200 IPS|2560×1600 IPS|2880×1800 OLED"), A("Battery", "Up to 8 hours|Up to 12 hours|Up to 15 hours|Up to 20 hours"), A("Weight", "1.3 kg|1.6 kg|1.8 kg")),
            S("desktops", "Desktops", "Настольные ПК|ڈیسک ٹاپ|أجهزة مكتبية", "desktop",
                [N("Mini PC", 399, 1199, "Form factor=Mini", "GPU=Integrated", "RAM=16 GB~32 GB"), N("Desktop PC", "Form factor=Compact tower~Mid tower"), N("Gaming PC", 1299, 3499, "Form factor=Mid tower", "GPU=12 GB discrete~16 GB discrete~24 GB discrete"), N("All-in-One PC", 799, 2299, "Form factor=All-in-one", "GPU=Integrated~8 GB discrete")],
                499, 2499,
                A("Form factor", "Compact tower|Mid tower"), A("RAM", "16 GB|32 GB|64 GB|128 GB"), A("Storage", "512 GB SSD|1 TB SSD|2 TB SSD|4 TB SSD"),
                A("CPU", "8-core, 4.8 GHz|12-core, 5.1 GHz|16-core, 5.6 GHz|24-core, 5.8 GHz"), A("GPU", "Integrated|8 GB discrete|12 GB discrete"),
                A("Ports", "USB-C, 4× USB-A, HDMI|Thunderbolt 4, 6× USB-A, 2× DisplayPort|2× USB-C, HDMI, 2.5 GbE")),
            S("monitors", "Monitors", "Мониторы|مانیٹر|شاشات", "monitor",
                [N("Monitor"), N("4K Monitor", 299, 999, "Resolution=3840×2160", "Screen size=27 in~32 in"), N("Gaming Monitor", 199, 1199, "Refresh rate=144 Hz~165 Hz~240 Hz", "Resolution=1920×1080~2560×1440"),
                 N("Ultrawide Monitor", 349, 1499, "Screen size=34 in~49 in", "Resolution=3440×1440~5120×1440")],
                129, 699,
                A("Screen size", "24 in|27 in|32 in"), A("Resolution", "1920×1080|2560×1440|3840×2160"), A("Refresh rate", "60 Hz|75 Hz|100 Hz|144 Hz"),
                A("Panel", "IPS|VA|OLED|Mini-LED"), A("Inputs", "HDMI, DisplayPort|HDMI, DisplayPort, USB-C 65 W|2× HDMI, DisplayPort, USB-C 90 W")),
            S("graphics-cards", "Graphics Cards", "Видеокарты|گرافکس کارڈ|بطاقات الرسوميات", "graphics-card", Ns("Graphics Card|Gaming Graphics Card|Workstation Graphics Card"), 199, 1899,
                A("Memory", "8 GB GDDR6|12 GB GDDR6X|16 GB GDDR6|16 GB GDDR7|24 GB GDDR6X"), A("Interface", "PCIe 4.0 x16|PCIe 5.0 x16"), A("Power", "165 W|220 W|285 W|350 W|450 W"),
                A("Outputs", "3× DisplayPort, HDMI|2× DisplayPort, 2× HDMI"), A("Length", "240 mm|285 mm|310 mm|336 mm")),
            S("storage-drives", "Storage Drives", "Накопители|اسٹوریج ڈرائیوز|وحدات التخزين", "storage-drive",
                [N("Portable SSD", 69, 449, "Interface=USB-C 10 Gbps~USB-C 20 Gbps", "Read speed=1,050 MB/s~2,000 MB/s", "Capacity=500 GB~1 TB~2 TB~4 TB"),
                 N("NVMe SSD", 49, 699, "Interface=PCIe 4.0 NVMe~PCIe 5.0 NVMe", "Read speed=7,000 MB/s~12,000 MB/s", "Capacity=500 GB~1 TB~2 TB~4 TB"),
                 N("External Hard Drive", 59, 299, "Interface=USB-C 10 Gbps", "Read speed=180 MB/s~220 MB/s", "Capacity=2 TB~4 TB~8 TB"),
                 N("SATA SSD", 39, 299, "Interface=SATA III", "Read speed=550 MB/s", "Capacity=500 GB~1 TB~2 TB~4 TB")],
                49, 699,
                A("Capacity", "500 GB|1 TB|2 TB|4 TB|8 TB"), A("Interface", "USB-C 10 Gbps"), A("Read speed", "550 MB/s"), A("Warranty", "3 years|5 years")),
            S("keyboards-mice", "Keyboards & Mice", "Клавиатуры и мыши|کی بورڈ اور ماؤس|لوحات المفاتيح والفأرات", "peripheral",
                [N("Mechanical Keyboard", 59, 249), N("Wireless Keyboard", 29, 129, $"Connection={Wireless}"), N("Wireless Mouse", 19, 99, $"Connection={Wireless}"),
                 N("Ergonomic Mouse", 29, 129), N("Wireless Keyboard and Mouse Set", 39, 149, $"Connection={Wireless}")],
                19, 249,
                A("Connection", "USB-C wired|Bluetooth|2.4 GHz and Bluetooth"), A("Colour", "Graphite|White|Sand|Slate blue"),
                A("Compatibility", "Windows, macOS|Windows, macOS, Linux|Windows, macOS, iPadOS, Android")),
        ]),
        new("audio", "Audio", "Аудио|آڈیو|الصوتيات", "music, calls and focus", ["Sonora", "Aurel", "Quietline", "Resona", "Tempo Audio"],
        [
            S("headphones", "Headphones", "Наушники|ہیڈفون|سماعات الرأس", "headphones",
                [N("Wireless Over-Ear Headphones", 79, 549, "Type=Over-ear", "Battery=30 hours~40 hours~60 hours", "Weight=250 g~310 g"),
                 N("Wireless On-Ear Headphones", 49, 249, "Type=On-ear", "Battery=30 hours~40 hours", "Weight=180 g~220 g", "Noise cancelling=None~Passive~Hybrid ANC"),
                 N("Wireless Earbuds", 29, 299, "Type=In-ear", "Battery=6 hours (24 with case)~8 hours (30 with case)", "Weight=5 g per bud"),
                 N("Studio Headphones", 79, 499, "Type=Over-ear~Open-back", "Battery=Wired", "Noise cancelling=None~Passive", "Connectivity=3.5 mm and 6.35 mm", "Weight=250 g~310 g")],
                29, 549,
                A("Type", "Over-ear"), A("Noise cancelling", "Passive|Hybrid ANC|Adaptive ANC"), A("Battery", "30 hours"),
                A("Connectivity", "Bluetooth 5.3|Bluetooth 5.4, multipoint"), A("Weight", "250 g")),
            S("speakers", "Speakers", "Колонки|اسپیکر|مكبرات الصوت", "speaker",
                [N("Portable Speaker", 39, 299, "Output=10 W~20 W~40 W", "Connectivity=Bluetooth", "Battery=12 hours~16 hours~24 hours"),
                 N("Bookshelf Speakers", 149, 899, "Output=40 W~100 W", "Connectivity=Optical, Bluetooth~Wi-Fi and Bluetooth", "Water resistance=None"),
                 N("Soundbar", 149, 1199, "Output=100 W~200 W~400 W", "Connectivity=HDMI eARC, Wi-Fi", "Water resistance=None"),
                 N("Smart Speaker", 49, 399, "Output=10 W~20 W~40 W", "Connectivity=Wi-Fi and Bluetooth", "Water resistance=None~IPX4")],
                39, 1199,
                A("Output", "20 W"), A("Connectivity", "Bluetooth"), A("Water resistance", "IPX4|IP67"), A("Battery", "Mains powered")),
            S("dacs-amps", "DACs & Amplifiers", "ЦАП и усилители|ڈی اے سی اور ایمپلیفائر|محولات ومضخمات الصوت", "dac",
                [N("USB DAC", 59, 399, "Inputs=USB-C"), N("Headphone Amplifier", 99, 999, "Output power=500 mW~1.5 W~4 W"), N("DAC and Amp", 149, 1499), N("Streaming DAC", 299, 1499, "Inputs=USB-C, Wi-Fi, optical")],
                59, 1499,
                A("Resolution", "24-bit / 192 kHz|32-bit / 384 kHz|32-bit / 768 kHz, DSD256"), A("Outputs", "3.5 mm|6.35 mm, 4.4 mm balanced|RCA, XLR"), A("Output power", "100 mW|500 mW|1.5 W|4 W"),
                A("Inputs", "USB-C|USB-C, optical, coaxial|USB-C, Bluetooth, optical")),
            S("microphones", "Microphones", "Микрофоны|مائیکروفون|الميكروفونات", "microphone",
                [N("USB Microphone", 39, 249, "Connection=USB-C~USB-C and XLR", "Resolution=24-bit / 96 kHz~32-bit float / 192 kHz"),
                 N("Dynamic Microphone", 59, 499, "Connection=XLR~USB-C and XLR", "Pattern=Cardioid~Supercardioid", "Resolution=Analogue (XLR)~24-bit / 96 kHz"),
                 N("Condenser Microphone", 79, 899, "Connection=XLR~USB-C and XLR", "Resolution=Analogue (XLR)~24-bit / 96 kHz"),
                 N("Lavalier Microphone", 29, 299, "Connection=3.5 mm TRRS~USB-C", "Pattern=Omnidirectional", "Includes=Clip and windscreen"),
                 N("Shotgun Microphone", 99, 899, "Pattern=Supercardioid", "Connection=XLR~3.5 mm TRRS", "Resolution=Analogue (XLR)", "Includes=Shock mount")],
                29, 899,
                A("Pattern", "Cardioid|Supercardioid|Multi-pattern"), A("Connection", "USB-C"), A("Resolution", "24-bit / 96 kHz"), A("Includes", "Desk stand|Shock mount|Pop filter and stand")),
        ]),
        new("electronics", "Electronics", "Электроника|الیکٹرانکس|الإلكترونيات", "staying connected", ["Parallax", "Lumio", "Corvid", "Tessera", "Orison"],
        [
            S("smartphones", "Smartphones", "Смартфоны|اسمارٹ فون|الهواتف الذكية", "smartphone",
                [N("Smartphone"), N("5G Smartphone", 299, 1399), N("Compact Smartphone", 399, 899, "Display=6.1 in OLED")], 149, 999,
                A("Storage", "128 GB|256 GB|512 GB|1 TB"), A("Display", "6.1 in OLED|6.4 in AMOLED|6.7 in OLED 120 Hz|6.8 in LTPO"), A("Camera", "50 MP|50 MP + 12 MP|50 MP + 48 MP + 12 MP|200 MP + 50 MP"),
                A("Battery", "4,000 mAh|4,500 mAh|5,000 mAh|5,500 mAh"), A("RAM", "6 GB|8 GB|12 GB|16 GB")),
            S("tablets", "Tablets", "Планшеты|ٹیبلٹ|الأجهزة اللوحية", "tablet",
                [N("Tablet"), N("Tablet Pro", 699, 1599, "Screen size=11 in~12.9 in~13 in", "Stylus=Supported~Included"),
                 N("Kids Tablet", 99, 199, "Screen size=8 in", "Storage=64 GB", "Connectivity=Wi-Fi", "Stylus=Not supported"),
                 N("Drawing Tablet", 399, 1299, "Screen size=12.9 in~13 in", "Stylus=Included")],
                199, 899,
                A("Screen size", "10.9 in|11 in"), A("Storage", "64 GB|128 GB|256 GB|512 GB"), A("Connectivity", "Wi-Fi|Wi-Fi + 5G"),
                A("Battery", "8 hours|10 hours|12 hours"), A("Stylus", "Not supported|Supported")),
            S("wearables", "Smartwatches & Trackers", "Умные часы и трекеры|اسمارٹ واچ اور ٹریکر|الساعات الذكية وأجهزة التتبع", "wearable",
                [N("Smartwatch", 149, 899, "Battery=2 days~7 days"), N("Fitness Tracker", 39, 199, "Case size=40 mm", "Battery=14 days~21 days", "Sensors=Heart rate~Heart rate, SpO2"),
                 N("Sports Watch", 199, 699, "Battery=7 days~14 days"), N("GPS Watch", 249, 899, "Battery=14 days~21 days", "Water resistance=10 ATM")],
                39, 899,
                A("Case size", "40 mm|42 mm|44 mm|46 mm"), A("Battery", "2 days|7 days|14 days"), A("Water resistance", "5 ATM|10 ATM"),
                A("Sensors", "Heart rate, SpO2|Heart rate, SpO2, ECG|Heart rate, SpO2, skin temperature")),
            S("televisions", "Televisions", "Телевизоры|ٹیلی ویژن|أجهزة التلفزيون", "television",
                [N("4K TV", 249, 1499, "Screen size=43 in~50 in~55 in~65 in~75 in"), N("OLED TV", 899, 3999, "Screen size=48 in~55 in~65 in~77 in", "Refresh rate=120 Hz~144 Hz"),
                 N("QLED TV", 449, 2499), N("8K TV", 1999, 5999, "Screen size=65 in~75 in~85 in", "Resolution=8K", "Refresh rate=120 Hz")],
                249, 2499,
                A("Screen size", "50 in|55 in|65 in|75 in|85 in"), A("Resolution", "4K UHD"), A("Refresh rate", "60 Hz|120 Hz"),
                A("HDR", "HDR10|HDR10, HLG|HDR10+, HLG"), A("Energy rating", "E|F|G")),
        ]),
        new("home-kitchen", "Home & Kitchen", "Дом и кухня|گھر اور باورچی خانہ|المنزل والمطبخ", "everyday living", ["Hearth & Co", "Oakline", "Casa Verde", "Linden", "Brook Home"],
        [
            S("furniture", "Furniture", "Мебель|فرنیچر|الأثاث", "furniture",
                [N("Sofa", 499, 2499, "Material=Bouclé fabric~Linen blend~Velvet", "Dimensions=210 × 95 × 85 cm~180 × 90 × 80 cm"),
                 N("Armchair", 199, 999, "Material=Bouclé fabric~Linen blend~Velvet", "Dimensions=80 × 80 × 75 cm"),
                 N("Dining Table", 249, 1499, "Dimensions=160 × 90 × 76 cm~120 × 80 × 76 cm"), N("Bookshelf", 79, 599, "Dimensions=80 × 35 × 180 cm"),
                 N("Bed Frame", 249, 1299, "Dimensions=160 × 200 × 35 cm~180 × 200 × 35 cm"), N("Coffee Table", 99, 699, "Dimensions=120 × 60 × 45 cm~90 × 90 × 40 cm")],
                79, 2499,
                A("Material", "Solid oak|Walnut veneer|Powder-coated steel"), A("Dimensions", "80 × 80 × 75 cm"), A("Colour", "Natural|Charcoal|Sand|Olive|White"),
                A("Assembly", "Required, 30 minutes|Required, 1 hour|Arrives assembled")),
            S("lighting", "Lighting", "Освещение|روشنی|الإضاءة", "lamp",
                [N("Floor Lamp", 49, 449, "Power=Mains"), N("Table Lamp", 25, 249), N("Pendant Light", 39, 399, "Power=Mains", "Control=Switch~App and voice"),
                 N("Desk Lamp", 19, 199), N("LED Strip", 15, 99, "Power=Mains~USB-C", "Control=App and voice", "Brightness=400 lm~800 lm")],
                15, 449,
                A("Brightness", "400 lm|800 lm|1,200 lm|2,000 lm"), A("Colour temperature", "2700 K|3000 K|2700–6500 K tunable"), A("Power", "Mains|USB-C|Rechargeable"),
                A("Control", "Switch|Touch dimmer|App and voice")),
            S("home-storage", "Storage & Organisation", "Хранение и порядок|اسٹوریج اور ترتیب|التخزين والتنظيم", "storage",
                [N("Storage Box Set", 15, 79, "Pieces=3~6~12", "Dimensions=30 × 20 × 15 cm~40 × 30 × 25 cm"), N("Shelving Unit", 49, 299, "Pieces=1", "Material=Powder-coated steel~Bamboo", "Dimensions=80 × 35 × 180 cm~90 × 40 × 200 cm"),
                 N("Drawer Organiser", 9, 49, "Pieces=3~6~12", "Dimensions=30 × 20 × 15 cm"), N("Laundry Basket", 15, 69, "Pieces=1", "Material=Seagrass~Recycled plastic~Bamboo", "Dimensions=40 × 30 × 55 cm"),
                 N("Wardrobe Organiser", 19, 99, "Pieces=1~3", "Material=Felt~Bamboo", "Dimensions=30 × 30 × 80 cm")],
                9, 299,
                A("Pieces", "1"), A("Material", "Bamboo|Recycled plastic|Powder-coated steel|Seagrass|Felt"), A("Dimensions", "40 × 30 × 25 cm")),
            S("cleaning", "Cleaning", "Уборка|صفائی|التنظيف", "vacuum",
                [N("Cordless Vacuum", 149, 699, "Suction=120 AW~180 AW~230 AW", "Runtime=40 minutes~60 minutes", "Bin capacity=0.5 L~0.75 L"),
                 N("Robot Vacuum", 199, 999, "Suction=2,500 Pa~5,000 Pa~8,000 Pa", "Runtime=120 minutes~180 minutes", "Bin capacity=0.3 L~0.4 L"),
                 N("Handheld Vacuum", 39, 149, "Suction=10 kPa~15 kPa", "Runtime=20 minutes~30 minutes", "Bin capacity=0.3 L"),
                 N("Wet and Dry Vacuum", 79, 299, "Suction=20 kPa~24 kPa", "Runtime=Corded", "Bin capacity=15 L~20 L")],
                39, 999,
                A("Suction", "120 AW"), A("Runtime", "40 minutes"), A("Bin capacity", "0.5 L"), A("Filter", "Washable|HEPA")),
            S("cookware", "Cookware", "Посуда для готовки|کھانا پکانے کے برتن|أواني الطهي", "cookware",
                [N("Frying Pan", 19, 199, "Size=20 cm~24 cm~28 cm"), N("Saucepan Set", 79, 449, "Size=3-piece set~5-piece set"),
                 N("Dutch Oven", 59, 399, "Size=24 cm, 4.2 L~28 cm, 6.7 L", "Material=Enamelled cast iron~Cast iron"), N("Wok", 29, 149, "Size=30 cm~32 cm", "Material=Carbon steel~Cast iron"),
                 N("Stock Pot", 39, 199, "Size=6 L~8 L", "Material=Tri-ply stainless steel")],
                19, 449,
                A("Size", "24 cm"), A("Material", "Cast iron|Tri-ply stainless steel|Carbon steel|Hard-anodised aluminium|Enamelled cast iron"), A("Hobs", "All, including induction|Gas and electric"),
                A("Oven safe", "Up to 180 °C|Up to 260 °C")),
            S("coffee-equipment", "Coffee Equipment", "Всё для кофе|کافی کا سامان|أدوات القهوة", "coffee",
                [N("Espresso Machine", 199, 1299, "Capacity=1.5 l~2 l", "Power=1,350 W~1,600 W", "Material=Stainless steel"),
                 N("Burr Grinder", 49, 499, "Capacity=250 g hopper~400 g hopper", "Power=Manual~150 W", "Material=Stainless steel~Ceramic"),
                 N("Pour-Over Kettle", 29, 179, "Capacity=600 ml~1 l", "Power=Stovetop~1,000 W", "Material=Stainless steel"),
                 N("French Press", 25, 79, "Capacity=350 ml~1 l", "Power=Manual", "Material=Borosilicate glass~Stainless steel"),
                 N("Drip Coffee Maker", 49, 299, "Capacity=1.25 l~1.8 l", "Power=1,000 W", "Material=Stainless steel~Borosilicate glass")],
                25, 1299,
                A("Capacity", "1 l"), A("Power", "Manual"), A("Material", "Stainless steel|Borosilicate glass|Ceramic")),
            S("utensils", "Utensils", "Кухонные принадлежности|باورچی خانے کے اوزار|أدوات المطبخ", "utensils",
                [N("Chef's Knife", 29, 249, "Pieces=1", "Material=German stainless steel~Japanese VG-10 steel", "Dishwasher safe=Hand wash only"),
                 N("Knife Set", 79, 399, "Pieces=5~8", "Material=German stainless steel~Japanese VG-10 steel", "Dishwasher safe=Hand wash only"),
                 N("Utensil Set", 15, 79, "Pieces=5~8~12", "Material=Silicone~Acacia wood~Bamboo"), N("Cutting Board", 15, 99, "Pieces=1~3", "Material=Acacia wood~Bamboo", "Dishwasher safe=Hand wash only"),
                 N("Measuring Set", 9, 39, "Pieces=8~12", "Material=German stainless steel~Silicone", "Dishwasher safe=Yes")],
                9, 399,
                A("Pieces", "1"), A("Material", "Silicone"), A("Dishwasher safe", "Yes|Hand wash only")),
            S("kitchen-appliances", "Small Kitchen Appliances", "Малая кухонная техника|باورچی خانے کے چھوٹے آلات|أجهزة المطبخ الصغيرة", "kitchen-appliance",
                [N("Air Fryer", 59, 249, "Capacity=4.5 L~5.5 L~8 L", "Power=1,500 W~1,800 W", "Programs=6 presets~8 presets~12 presets"),
                 N("Blender", 39, 499, "Capacity=1 L~1.5 L~2 L", "Power=800 W~1,200 W~1,500 W", "Programs=Manual~6 presets"),
                 N("Stand Mixer", 149, 699, "Capacity=4.5 L~5.5 L", "Power=800 W~1,200 W", "Programs=Manual"),
                 N("Toaster", 29, 199, "Capacity=2-slice~4-slice", "Power=900 W~1,500 W", "Programs=Manual~6 presets"),
                 N("Electric Kettle", 29, 149, "Capacity=1 L~1.7 L", "Power=2,200 W~3,000 W", "Programs=Manual~6 presets"),
                 N("Rice Cooker", 39, 299, "Capacity=1 L~1.8 L", "Power=500 W~800 W", "Programs=6 presets~12 presets")],
                29, 699,
                A("Capacity", "1 L"), A("Power", "1,200 W"), A("Programs", "Manual")),
        ]),
        new("books", "Books", "Книги|کتابیں|الكتب", "reading", ["Harbor Press", "Foundry Books", "Lantern House", "Meridian Editions", "Quarry Press", "Bellweather"],
        [
            Books("fiction", "Fiction", "Художественная литература|فکشن|الروايات",
                "The Quiet|The Last|The Hidden|The Silver|The Winter|The Burning|The Forgotten|The Distant|The Salt|The Paper|The Glass|The Long",
                "Harbor|Orchard|Lighthouse|Garden|Archive|Crossing|River|Season|Letters|Tide|Station|Summer"),
            Books("non-fiction", "Non-Fiction", "Нон-фикшн|غیر افسانوی|الكتب غير الروائية",
                "Focused Work|Better Sleep|Slow Cooking|Clear Thinking|Small Habits|Negotiation|Personal Finance|Walking|Public Speaking|Minimal Living",
                "Made Simple|A Practical Guide|for Busy People|That Lasts|Every Day|in 30 Days"),
            Books("computing-books", "Computing", "Компьютерная литература|کمپیوٹنگ کی کتابیں|كتب الحوسبة",
                "Distributed Systems|Rust|PostgreSQL|TypeScript|Kubernetes|Compilers|Machine Learning|Web Security|Algorithms|Data Engineering|Go|Functional Programming",
                "in Practice|Field Guide|Handbook|from Scratch|Patterns|Fundamentals|for Engineers|Deep Dive"),
            Books("science", "Science", "Наука|سائنس|العلوم",
                "The Cosmos|Genetics|Climate|Quantum Physics|The Brain|Evolution|Oceans|Chemistry|Mathematics|Ecology",
                "Explained|A Short Guide|Revisited|for the Curious|An Introduction|and Everyday Life"),
            Books("history", "History", "История|تاریخ|التاريخ",
                "The Silk Road|Empires of the Sea|The Industrial Age|Ancient Cities|The Printing Press|Medieval Trade|The Space Race|Rivers and Kingdoms",
                "A History|1850–1950|Rise and Fall|A New History|in Twelve Objects"),
        ]),
        new("appliances", "Appliances", "Бытовая техника|گھریلو آلات|الأجهزة المنزلية", "a well-run home", ["Frostline", "Kelvin & Hart", "Aerona", "Whitmore", "Calder"],
        [
            S("refrigerators", "Refrigerators", "Холодильники|ریفریجریٹر|الثلاجات", "refrigerator",
                [N("Fridge Freezer", 399, 1299, "Capacity=250 L~350 L", "Door configuration=Top freezer~Bottom freezer", "Dimensions=60 × 66 × 186 cm~70 × 70 × 200 cm"),
                 N("French-Door Refrigerator", 1199, 3299, "Capacity=450 L~600 L", "Door configuration=French door", "Dimensions=91 × 72 × 178 cm"),
                 N("Side-by-Side Refrigerator", 999, 2799, "Capacity=450 L~600 L", "Door configuration=Side by side", "Dimensions=91 × 72 × 178 cm"),
                 N("Under-Counter Fridge", 299, 699, "Capacity=130 L", "Door configuration=Single door", "Dimensions=60 × 60 × 85 cm")],
                299, 3299,
                A("Capacity", "350 L"), A("Energy rating", "A|B|C|D|E"), A("Door configuration", "Bottom freezer"),
                A("Compressor", "Inverter|Linear inverter|Conventional"), A("Dimensions", "60 × 66 × 186 cm")),
            S("freezers", "Freezers", "Морозильники|فریزر|المجمدات", "freezer",
                [N("Chest Freezer", 199, 799, "Capacity=200 L~300 L~400 L", "Dimensions=110 × 70 × 85 cm~140 × 70 × 85 cm", "Defrost=Manual"),
                 N("Upright Freezer", 399, 1499, "Capacity=200 L~300 L", "Dimensions=60 × 65 × 170 cm~60 × 65 × 186 cm"),
                 N("Under-Counter Freezer", 199, 599, "Capacity=85 L~100 L", "Dimensions=55 × 57 × 85 cm")],
                199, 1499,
                A("Capacity", "200 L"), A("Energy rating", "A|B|C|D|E"), A("Defrost", "Manual|Frost-free"), A("Compressor", "Inverter|Conventional"), A("Dimensions", "60 × 65 × 170 cm")),
            S("microwaves", "Microwaves", "Микроволновые печи|مائیکروویو|أفران الميكروويف", "microwave",
                [N("Solo Microwave", 69, 199, "Type=Solo", "Capacity=20 L~23 L"), N("Grill Microwave", 99, 299, "Type=Grill", "Capacity=23 L~25 L"),
                 N("Combination Microwave", 199, 599, "Type=Combination", "Capacity=25 L~32 L", "Power=900 W~1,000 W"), N("Built-in Microwave", 299, 699, "Type=Built-in", "Capacity=25 L~32 L")],
                69, 699,
                A("Capacity", "20 L|23 L|25 L|32 L"), A("Power", "700 W|800 W|900 W|1,000 W"), A("Type", "Solo")),
            S("washing-machines", "Washing Machines", "Стиральные машины|واشنگ مشین|الغسالات", "washer",
                [N("Washing Machine", 299, 1199), N("Washer Dryer", 499, 1499, "Capacity=9 kg~10 kg")], 299, 1499,
                A("Capacity", "7 kg|8 kg|9 kg|10 kg|12 kg"), A("Spin speed", "1,200 rpm|1,400 rpm|1,600 rpm"), A("Energy rating", "A|B|C|D"),
                A("Programs", "14|16|20"), A("Noise", "48 dB|52 dB|56 dB")),
            S("tumble-dryers", "Tumble Dryers", "Сушильные машины|کپڑے سکھانے والی مشین|مجففات الملابس", "dryer",
                [N("Heat Pump Dryer", 499, 1299, "Type=Heat pump", "Energy rating=A+++~A++"), N("Condenser Dryer", 299, 699, "Type=Condenser", "Energy rating=B"),
                 N("Vented Dryer", 249, 499, "Type=Vented", "Energy rating=C")],
                249, 1299,
                A("Capacity", "7 kg|8 kg|9 kg|10 kg"), A("Energy rating", "B"), A("Type", "Condenser")),
            S("air-conditioners", "Air Conditioners", "Кондиционеры|ایئر کنڈیشنر|مكيفات الهواء", "air-conditioner",
                [N("Split Air Conditioner", 499, 1899, "Type=Split inverter", "Noise=19 dB~24 dB~32 dB"), N("Portable Air Conditioner", 249, 799, "Type=Portable", "Cooling capacity=9,000 BTU~12,000 BTU", "Noise=48 dB~52 dB"),
                 N("Window Air Conditioner", 199, 699, "Type=Window", "Cooling capacity=9,000 BTU~12,000 BTU~18,000 BTU", "Noise=48 dB~55 dB")],
                199, 1899,
                A("Cooling capacity", "9,000 BTU|12,000 BTU|18,000 BTU|24,000 BTU"), A("Energy rating", "A+++|A++|A+|A"), A("Type", "Split inverter"),
                A("Noise", "24 dB"), A("Refrigerant", "R32|R290")),
        ]),
        new("gaming", "Gaming", "Игры|گیمنگ|الألعاب الإلكترونية", "long sessions and fast play", ["Vertex Play", "Nightforge", "Pixelcraft", "Rogue Arc", "Blinkstar"],
        [
            S("consoles", "Consoles", "Игровые консоли|گیمنگ کنسول|أجهزة الألعاب", "console",
                [N("Game Console", 399, 699, "Storage=825 GB~1 TB~2 TB", "Resolution=4K 120 Hz"), N("Handheld Console", 199, 649, "Storage=64 GB~256 GB~512 GB", "Resolution=720p~1080p"),
                 N("Retro Console", 59, 149, "Storage=16 GB~64 GB", "Resolution=720p~1080p", "Edition=Standard")],
                59, 699,
                A("Storage", "1 TB"), A("Resolution", "4K 120 Hz"), A("Edition", "Standard|Digital|Bundle with two controllers")),
            S("controllers", "Controllers", "Контроллеры|کنٹرولر|وحدات التحكم", "controller",
                [N("Wireless Controller", 39, 99, "Connection=2.4 GHz wireless~Bluetooth", "Battery=20 hours~40 hours"), N("Pro Controller", 99, 229, "Connection=2.4 GHz wireless~Bluetooth", "Battery=20 hours~40 hours"),
                 N("Arcade Stick", 99, 299, "Connection=USB-C wired", "Battery=Wired"), N("Racing Wheel", 149, 399, "Connection=USB-C wired", "Battery=Wired", "Compatibility=PC~PC and console")],
                29, 399,
                A("Connection", "USB-C wired"), A("Compatibility", "PC|PC and console|PC, console and mobile"), A("Battery", "Wired")),
            S("gaming-headsets", "Gaming Headsets", "Игровые гарнитуры|گیمنگ ہیڈسیٹ|سماعات الألعاب", "headset",
                [N("Gaming Headset", 29, 149, "Connection=USB-C wired~3.5 mm wired"), N("Wireless Gaming Headset", 79, 349, "Connection=2.4 GHz wireless~2.4 GHz and Bluetooth")],
                29, 349,
                A("Connection", "USB-C wired"), A("Audio", "Stereo|Virtual 7.1|Spatial audio"), A("Microphone", "Detachable boom|Flip-to-mute boom|Retractable")),
            S("gaming-chairs", "Gaming Chairs", "Игровые кресла|گیمنگ کرسیاں|كراسي الألعاب", "chair", Ns("Gaming Chair|Ergonomic Gaming Chair"), 149, 699,
                A("Material", "PU leather|Breathable fabric|Mesh"), A("Max load", "120 kg|136 kg|150 kg|180 kg"), A("Recline", "135°|155°|165°"), A("Armrests", "2D|3D|4D")),
        ]),
        new("networking", "Networking", "Сетевое оборудование|نیٹ ورکنگ|الشبكات", "fast, reliable connections", ["Relay", "Meshwork", "Linkstar", "Bandwave", "Portside"],
        [
            S("routers", "Routers", "Роутеры|راؤٹر|أجهزة التوجيه", "router",
                [N("Wi-Fi Router", 49, 399), N("Gaming Router", 199, 599, "Wi-Fi=Wi-Fi 6E AXE7800~Wi-Fi 7 BE9300~Wi-Fi 7 BE19000", "Ports=2.5 GbE WAN, 4× gigabit~10 GbE WAN, 4× 2.5 GbE"),
                 N("Travel Router", 39, 129, "Wi-Fi=Wi-Fi 6 AX1800~Wi-Fi 6 AX3000", "Ports=2× gigabit", "Coverage=Up to 100 m²")],
                39, 599,
                A("Wi-Fi", "Wi-Fi 6 AX3000|Wi-Fi 6 AX5400|Wi-Fi 6E AXE7800|Wi-Fi 7 BE9300"), A("Ports", "4× gigabit|2.5 GbE WAN, 4× gigabit"), A("Coverage", "Up to 150 m²|Up to 250 m²|Up to 350 m²")),
            S("mesh-wifi", "Mesh Wi-Fi", "Mesh-системы|میش وائی فائی|أنظمة Wi-Fi الشبكية", "mesh",
                [N("Mesh Wi-Fi System", 149, 899, "Pack=2-pack~3-pack"), N("Mesh Wi-Fi Extender", 79, 299, "Pack=1-pack", "Coverage=Up to 150 m²")], 79, 899,
                A("Pack", "2-pack"), A("Wi-Fi standard", "Wi-Fi 6|Wi-Fi 6E|Wi-Fi 7"), A("Coverage", "Up to 300 m²|Up to 450 m²|Up to 600 m²")),
            S("network-switches", "Network Switches", "Коммутаторы|نیٹ ورک سوئچ|محولات الشبكة", "switch",
                [N("Unmanaged Switch", 19, 199, "PoE=None"), N("Managed Switch", 79, 799), N("PoE Switch", 99, 799, "PoE=60 W budget~120 W budget~250 W budget")], 19, 799,
                A("Ports", "5 ports|8 ports|16 ports|24 ports"), A("Speed", "Gigabit|2.5 GbE|10 GbE"), A("PoE", "None|60 W budget|120 W budget")),
            S("network-storage", "Network Storage", "Сетевые хранилища|نیٹ ورک اسٹوریج|التخزين الشبكي", "nas",
                [N("2-Bay NAS", 199, 599, "Bays=2"), N("4-Bay NAS", 399, 1199, "Bays=4"), N("6-Bay NAS", 799, 1499, "Bays=6", "Network=2× 2.5 GbE~10 GbE"),
                 N("Personal Cloud Drive", 149, 399, "Bays=1", "Network=Gigabit", "CPU=4-core, 2.0 GHz")],
                149, 1499,
                A("Bays", "2"), A("Network", "Gigabit|2× 2.5 GbE|10 GbE"), A("CPU", "4-core, 2.0 GHz|4-core, 2.6 GHz|8-core, 3.0 GHz")),
        ]),
        new("office", "Office", "Офис|دفتر|المكتب", "focused work", ["Quillmark", "Deskwise", "Ledger & Line", "Paperfold", "Stationhouse"],
        [
            S("desks", "Desks", "Столы|میزیں|المكاتب", "desk",
                [N("Standing Desk", 249, 799, "Height=Manual 70–120 cm~Electric 62–127 cm"), N("Writing Desk", 99, 499, "Height=Fixed 75 cm", "Width=100 cm~120 cm"),
                 N("Corner Desk", 149, 599, "Height=Fixed 75 cm", "Width=140 cm~160 cm"), N("Electric Standing Desk", 349, 1199, "Height=Electric 62–127 cm")],
                99, 1199,
                A("Width", "120 cm|140 cm|160 cm|180 cm"), A("Height", "Fixed 75 cm"), A("Top", "Oak veneer|Bamboo|Walnut laminate|White laminate"), A("Max load", "70 kg|100 kg|125 kg")),
            S("office-chairs", "Office Chairs", "Офисные кресла|دفتر کی کرسیاں|كراسي المكتب", "chair",
                [N("Ergonomic Office Chair", 249, 1299), N("Task Chair", 89, 299, "Armrests=Fixed~3D"), N("Mesh Office Chair", 149, 599, "Back=Mesh"), N("Executive Chair", 299, 999, "Back=Upholstered~Leather")],
                89, 1299,
                A("Back", "Mesh|Upholstered|Leather"), A("Lumbar support", "Fixed|Adjustable|Dynamic"), A("Armrests", "Fixed|3D|4D"), A("Max load", "110 kg|136 kg|150 kg")),
            S("printers", "Printers", "Принтеры|پرنٹر|الطابعات", "printer",
                [N("Laser Printer", 129, 599, "Type=Mono laser~Colour laser"), N("Inkjet Printer", 59, 249, "Type=Inkjet~Ink tank", "Speed=15 ppm~20 ppm"),
                 N("All-in-One Printer", 99, 899), N("Photo Printer", 149, 699, "Type=Inkjet", "Speed=10 ppm~15 ppm")],
                59, 899,
                A("Type", "Mono laser|Colour laser|Inkjet|Ink tank"), A("Speed", "20 ppm|30 ppm|40 ppm"), A("Connectivity", "USB|Wi-Fi|Wi-Fi and Ethernet"), A("Duplex", "Manual|Automatic")),
            S("notebooks-pens", "Notebooks & Pens", "Тетради и ручки|نوٹ بک اور قلم|الدفاتر والأقلام", "stationery",
                [N("Notebook", 5, 39), N("Planner", 12, 49, "Pack=1"), N("Sketchbook", 8, 49, "Size=A4~A5"),
                 N("Fountain Pen", 19, 149, "Size=Fine nib~Medium nib", "Pack=1"), N("Gel Pen Set", 3, 29, "Size=0.5 mm~0.7 mm", "Pack=3~5~10")],
                3, 149,
                A("Size", "A4|A5|B5|Pocket"), A("Pack", "1|3|5"), A("Colour", "Black|Navy|Forest green|Terracotta|Assorted")),
        ]),
        new("sports-fitness", "Sports & Fitness", "Спорт и фитнес|کھیل اور فٹنس|الرياضة واللياقة", "training at home and outdoors", ["Ironform", "Peakline", "Kinetic", "Flowfit", "Stridewell"],
        [
            S("yoga-pilates", "Yoga & Pilates", "Йога и пилатес|یوگا اور پیلاٹیز|اليوغا والبيلاتس", "yoga",
                [N("Yoga Mat", 19, 149), N("Travel Yoga Mat", 25, 79, "Thickness=1.5 mm~3 mm"), N("Pilates Mat", 29, 99, "Thickness=10 mm~15 mm", "Material=Foam~TPE"),
                 N("Yoga Block Set", 12, 39, "Thickness=7.5 cm~10 cm", "Material=Cork~Foam")],
                12, 149,
                A("Material", "Natural rubber|TPE|Cork"), A("Thickness", "4 mm|5 mm|6 mm"), A("Colour", "Sage|Charcoal|Sand|Plum|Ocean")),
            S("strength-training", "Strength Training", "Силовые тренировки|طاقت کی تربیت|تمارين القوة", "strength",
                [N("Adjustable Dumbbells", 149, 599, "Weight=2–24 kg per dumbbell~2–32 kg per dumbbell", "Material=Rubber-coated steel"),
                 N("Kettlebell", 19, 129, "Weight=8 kg~12 kg~16 kg~24 kg", "Material=Cast iron~Rubber-coated steel"),
                 N("Weight Bench", 99, 499, "Weight=18 kg~25 kg", "Material=Powder-coated steel"),
                 N("Resistance Band Set", 15, 59, "Weight=5–35 kg resistance~10–70 kg resistance", "Material=Latex~Fabric-covered latex", "Max load=Not applicable"),
                 N("Barbell Set", 149, 899, "Weight=30 kg~50 kg~100 kg", "Material=Cast iron~Rubber-coated steel")],
                15, 899,
                A("Weight", "12 kg"), A("Material", "Cast iron"), A("Max load", "150 kg|250 kg|300 kg")),
            S("cardio-machines", "Cardio Machines", "Кардиотренажёры|کارڈیو مشینیں|أجهزة الكارديو", "cardio", Ns("Treadmill|Exercise Bike|Rowing Machine|Elliptical Trainer"), 299, 2999,
                A("Resistance", "8 levels|16 levels|24 levels|32 levels"), A("Max user weight", "110 kg|130 kg|150 kg"), A("Folding", "Yes|No"), A("Display", "LCD|7 in touchscreen|22 in touchscreen")),
            S("cycling", "Cycling", "Велоспорт|سائیکلنگ|ركوب الدراجات", "bike",
                [N("Road Bike", 699, 4999, "Frame=Aluminium~Carbon", "Brakes=Rim~Hydraulic disc", "Gears=2×11~2×12"), N("Gravel Bike", 799, 3999, "Brakes=Mechanical disc~Hydraulic disc", "Gears=1×11~1×12~2×11"),
                 N("Hybrid Bike", 399, 1299, "Frame=Aluminium~Steel", "Brakes=Rim~Mechanical disc"), N("E-Bike", 1299, 4999, "Frame=Aluminium", "Brakes=Hydraulic disc", "Gears=1×11~Hub, 8-speed")],
                399, 4999,
                A("Frame", "Aluminium|Carbon|Steel"), A("Frame size", "S|M|L|XL"), A("Gears", "1×11|2×11"), A("Brakes", "Hydraulic disc")),
        ]),
        new("outdoor-camping", "Outdoor & Camping", "Туризм и кемпинг|آؤٹ ڈور اور کیمپنگ|الرحلات والتخييم", "trails, campsites and long weekends", ["Trailhead", "Northridge", "Basecamp Co", "Wildmere", "Cairn"],
        [
            S("tents", "Tents", "Палатки|خیمے|الخيام", "tent",
                [N("Backpacking Tent", 149, 499, "Sleeps=1 person~2 people", "Weight=1.4 kg~1.8 kg"), N("Family Tent", 199, 899, "Sleeps=4 people~6 people~8 people", "Weight=8 kg~12 kg"),
                 N("Ultralight Tent", 299, 799, "Sleeps=1 person~2 people", "Weight=0.9 kg~1.1 kg"), N("Dome Tent", 59, 299, "Sleeps=2 people~4 people", "Weight=2.5 kg~4.5 kg")],
                59, 899,
                A("Sleeps", "2 people"), A("Season", "3-season|3-season|4-season"), A("Weight", "1.8 kg"), A("Waterproof rating", "2,000 mm|3,000 mm|5,000 mm")),
            S("sleeping-bags", "Sleeping Bags", "Спальные мешки|سلیپنگ بیگ|أكياس النوم", "sleeping-bag",
                [N("Sleeping Bag", 29, 149, "Fill=Synthetic", "Weight=1.1 kg~1.6 kg~2.2 kg"), N("Down Sleeping Bag", 199, 599, "Fill=Duck down~Goose down", "Weight=0.7 kg~1.1 kg"),
                 N("Mummy Sleeping Bag", 59, 349)],
                29, 599,
                A("Comfort temperature", "+10 °C|+5 °C|0 °C|−5 °C|−15 °C"), A("Fill", "Synthetic|Duck down|Goose down"), A("Weight", "0.9 kg|1.2 kg|1.6 kg")),
            S("hiking-backpacks", "Hiking Backpacks", "Туристические рюкзаки|ہائیکنگ بیگ|حقائب المشي", "backpack",
                [N("Daypack", 35, 149, "Capacity=20 L~25 L~30 L"), N("Hiking Backpack", 79, 249, "Capacity=30 L~45 L"), N("Trekking Pack", 149, 399, "Capacity=55 L~65 L~75 L", "Back system=Adjustable")],
                35, 399,
                A("Capacity", "30 L"), A("Back system", "Ventilated mesh|Adjustable|Fixed"), A("Rain cover", "Included|Not included")),
            S("camp-kitchen", "Camp Kitchen", "Кухня для кемпинга|کیمپ کچن|مطبخ التخييم", "camp-kitchen",
                [N("Camping Stove", 29, 199, "Capacity=Single burner~Two burners", "Weight=85 g~410 g~2.3 kg"), N("Cook Set", 25, 149, "Capacity=750 ml~1.2 L~2 L", "Weight=220 g~410 g"),
                 N("Insulated Mug", 15, 49, "Capacity=350 ml~450 ml", "Material=Stainless steel~Titanium", "Weight=85 g~220 g"),
                 N("Water Filter", 29, 99, "Capacity=1,000 L filter life~1,500 L filter life", "Material=BPA-free plastic", "Weight=85 g~220 g")],
                15, 199,
                A("Material", "Titanium|Aluminium|Stainless steel"), A("Capacity", "750 ml"), A("Weight", "220 g")),
        ]),
        new("garden", "Garden", "Сад|باغبانی|الحديقة", "growing and tending outdoors", ["Greenhaven", "Fernwood", "Rootstock", "Meadowline", "Thistle"],
        [
            S("lawn-mowers", "Lawn Mowers", "Газонокосилки|گھاس کاٹنے کی مشینیں|جزازات العشب", "mower",
                [N("Cordless Lawn Mower", 199, 699, "Power=36 V battery"), N("Petrol Lawn Mower", 249, 899, "Power=Petrol 140 cc~Petrol 170 cc", "Cutting width=42 cm~46 cm~51 cm"),
                 N("Robot Lawn Mower", 499, 1499, "Power=18 V battery", "Cutting width=18 cm~22 cm", "Grass box=Mulching, no box"),
                 N("Electric Lawn Mower", 99, 299, "Power=1,200 W electric~1,600 W electric", "Lawn size=Up to 250 m²~Up to 500 m²")],
                99, 1499,
                A("Cutting width", "32 cm|37 cm|42 cm"), A("Power", "36 V battery"), A("Lawn size", "Up to 250 m²|Up to 500 m²|Up to 1,000 m²"), A("Grass box", "35 L|45 L|60 L")),
            S("garden-tools", "Garden Tools", "Садовый инструмент|باغبانی کے اوزار|أدوات الحديقة", "garden-tool",
                [N("Pruning Shears", 9, 69, "Power=Manual", "Weight=0.2 kg~0.3 kg"), N("Hedge Trimmer", 59, 299, "Power=18 V battery~36 V battery~Mains", "Weight=2.5 kg~3.8 kg"),
                 N("Garden Spade", 19, 79, "Power=Manual", "Weight=1.2 kg~2 kg"), N("Leaf Blower", 49, 299, "Power=18 V battery~36 V battery~Mains", "Weight=1.8 kg~2.6 kg"),
                 N("Hose Reel", 29, 149, "Power=Manual", "Material=Powder-coated steel~Recycled plastic", "Weight=3.5 kg~6 kg")],
                9, 299,
                A("Power", "Manual"), A("Material", "Carbon steel|Stainless steel|Aluminium"), A("Weight", "1 kg")),
            S("planters", "Planters & Pots", "Кашпо и горшки|گملے|الأحواض والأصص", "planter",
                [N("Planter", 15, 149), N("Raised Bed", 59, 349, "Size=120 × 60 cm~180 × 90 cm", "Material=Cedar~Galvanised steel"), N("Plant Pot Set", 15, 79), N("Hanging Planter", 9, 49, "Size=15 cm~20 cm")],
                9, 349,
                A("Material", "Terracotta|Glazed ceramic|Recycled plastic|Galvanised steel|Cedar"), A("Size", "20 cm|30 cm|45 cm|60 cm"), A("Drainage", "Drainage hole|Self-watering reservoir")),
            S("watering", "Watering", "Полив|آبپاشی|الري", "watering",
                [N("Garden Hose", 19, 99, "Size=15 m~30 m", "Material=Reinforced PVC"), N("Sprinkler", 12, 79, "Size=Up to 150 m²~Up to 300 m²"),
                 N("Watering Can", 9, 49, "Size=5 L~10 L", "Material=Galvanised steel~Recycled plastic"), N("Drip Irrigation Kit", 29, 149, "Size=15 m~30 m", "Material=Recycled plastic")],
                9, 149,
                A("Size", "15 m"), A("Material", "Reinforced PVC|Galvanised steel|Recycled plastic"), A("Connection", "Standard ½ in|¾ in|Quick connect")),
        ]),
        new("tools-diy", "Tools & DIY", "Инструменты|اوزار اور مرمت|العدد والأدوات", "building, fixing and making", ["Torque", "Brightforge", "Anvil & Co", "Gritline", "Keystone Tools"],
        [
            S("drills-drivers", "Drills & Drivers", "Дрели и шуруповёрты|ڈرل اور ڈرائیور|المثاقب والمفكات", "drill",
                [N("Cordless Drill Driver", 49, 249, "Chuck=10 mm~13 mm", "Torque=35 Nm~60 Nm"), N("Combi Drill", 79, 349, "Chuck=13 mm", "Torque=60 Nm~90 Nm"),
                 N("Impact Driver", 79, 299, "Chuck=¼ in hex", "Torque=180 Nm~220 Nm"), N("SDS Hammer Drill", 99, 499, "Chuck=SDS-Plus", "Voltage=18 V~36 V", "Torque=2.2 J impact~3.0 J impact")],
                39, 499,
                A("Voltage", "12 V|18 V|20 V"), A("Torque", "60 Nm"), A("Batteries", "Body only|1× 2.0 Ah|2× 2.0 Ah|2× 4.0 Ah"), A("Chuck", "13 mm")),
            S("saws", "Saws", "Пилы|آریاں|المناشير", "saw",
                [N("Circular Saw", 59, 349, "Blade=165 mm~190 mm", "Max cut depth=55 mm~65 mm"), N("Jigsaw", 49, 249, "Blade=T-shank", "Max cut depth=100 mm~120 mm in wood"),
                 N("Mitre Saw", 149, 699, "Blade=216 mm~254 mm", "Power=1,500 W~1,800 W", "Max cut depth=65 mm~90 mm"), N("Reciprocating Saw", 69, 299, "Blade=150 mm~230 mm", "Max cut depth=150 mm~230 mm")],
                49, 699,
                A("Power", "18 V battery|750 W|1,200 W"), A("Blade", "165 mm"), A("Max cut depth", "65 mm")),
            S("hand-tools", "Hand Tools", "Ручной инструмент|دستی اوزار|الأدوات اليدوية", "hand-tool",
                [N("Screwdriver Set", 12, 79, "Pieces=6~12~40"), N("Socket Set", 29, 249, "Pieces=40~108", "Case=Hard case"), N("Claw Hammer", 12, 49, "Pieces=1", "Case=None", "Material=Hardened steel~Fibreglass handle"),
                 N("Spirit Level", 9, 59, "Pieces=1", "Case=None", "Material=Aluminium"), N("Plier Set", 19, 99, "Pieces=3~6")],
                7, 249,
                A("Pieces", "6"), A("Material", "Chrome vanadium steel|Hardened steel"), A("Case", "Roll pouch|Hard case")),
            S("tool-storage", "Tool Storage", "Хранение инструментов|اوزاروں کا اسٹوریج|تخزين الأدوات", "tool-storage",
                [N("Tool Box", 15, 99, "Dimensions=40 × 20 × 18 cm~56 × 30 × 26 cm", "Compartments=1~5"), N("Tool Chest", 199, 899, "Material=Steel", "Compartments=7~12", "Dimensions=68 × 46 × 100 cm"),
                 N("Tool Bag", 25, 119, "Material=Canvas~Ballistic nylon", "Compartments=12~24", "Dimensions=40 × 25 × 30 cm~50 × 30 × 35 cm"), N("Wall Pegboard", 29, 149, "Material=Steel", "Compartments=1", "Dimensions=120 × 60 cm")],
                15, 899,
                A("Material", "Steel|Polypropylene|Canvas|Ballistic nylon"), A("Compartments", "5"), A("Dimensions", "56 × 30 × 26 cm")),
        ]),
        new("automotive", "Automotive", "Автотовары|آٹوموٹیو|مستلزمات السيارات", "the road and the garage", ["Roadwise", "Motorline", "Apex Auto", "Gearhead", "Wayfarer"],
        [
            S("dash-cams", "Dash Cams", "Видеорегистраторы|ڈیش کیم|كاميرات السيارة", "dash-cam", Ns("Dash Cam|Front and Rear Dash Cam|Mirror Dash Cam"), 39, 399,
                A("Resolution", "1080p|1440p|4K"), A("Field of view", "130°|140°|160°"), A("GPS", "No|Built-in"), A("Storage", "microSD up to 128 GB|microSD up to 256 GB")),
            S("car-electronics", "Car Electronics", "Автоэлектроника|کار الیکٹرانکس|إلكترونيات السيارة", "car-electronics",
                [N("Car Charger", 9, 49, "Output=30 W~65 W~100 W", "Power=12 V socket"), N("Tyre Inflator", 29, 129, "Output=150 psi", "Power=12 V socket~Built-in battery"),
                 N("Jump Starter", 49, 199, "Output=1,000 A peak~2,000 A peak", "Power=Built-in battery"), N("Phone Mount", 12, 69, "Output=None~15 W wireless charging", "Power=None~12 V socket", "Compatibility=Phones up to 7 in")],
                9, 199,
                A("Output", "65 W"), A("Power", "12 V socket"), A("Compatibility", "All vehicles|12 V vehicles")),
            S("car-care", "Car Care", "Уход за автомобилем|گاڑی کی دیکھ بھال|العناية بالسيارة", "car-care",
                [N("Car Wash Kit", 19, 79, "Power=Manual", "Contents=6 pieces~10 pieces"), N("Pressure Washer", 99, 399, "Power=120 bar~150 bar", "Contents=1 piece~3 pieces", "Use=Exterior"),
                 N("Microfibre Towel Set", 9, 39, "Power=Manual", "Contents=3 pieces~6 pieces"), N("Car Vacuum", 29, 129, "Power=12 V~Built-in battery", "Contents=1 piece~3 pieces", "Use=Interior")],
                9, 399,
                A("Contents", "6 pieces"), A("Power", "Manual"), A("Use", "Exterior|Interior|Interior and exterior")),
            S("tyres", "Tyres", "Шины|ٹائر|الإطارات", "tyre",
                [N("All-Season Tyre", "Season=All-season"), N("Summer Tyre", "Season=Summer"), N("Winter Tyre", "Season=Winter", "Wet grip=A~B")], 59, 349,
                A("Size", "195/65 R15|205/55 R16|225/45 R17|235/55 R18"), A("Season", "All-season"), A("Fuel efficiency", "A|B|C|D"),
                A("Wet grip", "A|B|C"), A("Noise", "68 dB|70 dB|72 dB")),
        ]),
        new("baby", "Baby", "Детские товары|بچوں کا سامان|مستلزمات الأطفال", "the first years", ["Littlenest", "Bramble", "Tadpole", "Cradlewood", "Sprout"],
        [
            S("strollers", "Strollers", "Коляски|بچہ گاڑی|عربات الأطفال", "stroller",
                [N("Stroller", 199, 799), N("Travel System", 399, 1299, "Age=From birth", "Weight=10.5 kg~12 kg"), N("Compact Stroller", 149, 499, "Age=6 months+", "Weight=6.5 kg~7.5 kg", "Fold=Cabin-size fold"),
                 N("Jogging Stroller", 299, 799, "Age=6 months+", "Weight=10.5 kg~12 kg")],
                149, 1299,
                A("Age", "From birth|6 months+"), A("Weight", "8 kg|9.5 kg"), A("Fold", "One-hand fold|Two-hand fold")),
            S("car-seats", "Car Seats", "Автокресла|کار سیٹ|مقاعد السيارة للأطفال", "car-seat",
                [N("Infant Car Seat", 99, 349, "Group=i-Size 40–87 cm", "Rotation=Fixed"), N("Convertible Car Seat", 199, 599, "Group=i-Size 40–105 cm"), N("Booster Seat", 49, 199, "Group=i-Size 100–150 cm", "Rotation=Fixed")],
                49, 599,
                A("Group", "i-Size 40–105 cm"), A("Installation", "ISOFIX|Belt|ISOFIX or belt"), A("Rotation", "Fixed|360°")),
            S("nursery", "Nursery", "Детская комната|نرسری|غرفة الطفل", "nursery",
                [N("Cot", 149, 799, "Material=Beech wood~Pine", "Feature=Adjustable base~Converts to toddler bed", "Age=0–4 years"),
                 N("Baby Monitor", 49, 299, "Material=Recycled plastic", "Feature=Audio only~Video and audio"),
                 N("Changing Table", 99, 399, "Material=Beech wood~Pine", "Feature=Storage shelves~Fold-down top"), N("Baby Carrier", 39, 199, "Material=Organic cotton~Mesh", "Feature=Ergonomic hip seat", "Age=0–24 months")],
                39, 799,
                A("Age", "0–6 months|0–24 months|0–4 years"), A("Material", "Beech wood"), A("Feature", "Adjustable base")),
            S("feeding", "Feeding", "Кормление|خوراک|تغذية الرضع", "feeding",
                [N("Bottle Set", 12, 59, "Material=BPA-free plastic~Glass~Silicone", "Pieces=3~6~10", "Age=From birth"), N("High Chair", 59, 399, "Material=Beech wood~BPA-free plastic", "Pieces=1", "Age=6 months+"),
                 N("Bottle Warmer", 25, 79, "Material=BPA-free plastic", "Pieces=1", "Age=From birth"), N("Breast Pump", 49, 299, "Material=BPA-free plastic~Silicone", "Pieces=1", "Age=From birth")],
                12, 399,
                A("Material", "BPA-free plastic"), A("Age", "From birth"), A("Pieces", "1")),
        ]),
        new("toys-games", "Toys & Games", "Игрушки и игры|کھلونے اور کھیل|الألعاب والدمى", "play and learning", ["Brightblock", "Puzzlewood", "Kite & Kin", "Marble Lane", "Oddfellow Games"],
        [
            S("board-games", "Board Games", "Настольные игры|بورڈ گیمز|الألعاب اللوحية", "board-game",
                [N("Strategy Game", 29, 99, "Age=10+~14+", "Playing time=60 minutes~90 minutes~120 minutes"), N("Family Board Game", 19, 59, "Age=6+~8+", "Playing time=30 minutes~45 minutes"),
                 N("Card Game", 9, 29, "Playing time=15 minutes~30 minutes"), N("Party Game", 15, 39, "Players=4–10~3–8", "Age=10+~14+"), N("Cooperative Game", 25, 79, "Players=1–4~2–5")],
                9, 99,
                A("Players", "2–4|2–6|1–4"), A("Age", "8+|10+|14+"), A("Playing time", "30 minutes|60 minutes")),
            S("building-sets", "Building Sets", "Конструкторы|بلڈنگ سیٹ|ألعاب التركيب", "building-set",
                [N("Building Set", 12, 299), N("Construction Kit", 19, 149, "Theme=Vehicles~Architecture", "Age=6+~9+~12+"), N("Marble Run", 25, 129, "Pieces=60~150~250", "Theme=Marble run"),
                 N("Magnetic Tiles", 29, 149, "Pieces=40~60~100", "Age=3+", "Theme=Shapes and colours")],
                12, 299,
                A("Pieces", "250|500|1,200|2,500"), A("Age", "6+|9+|12+|18+"), A("Theme", "City|Space|Vehicles|Architecture|Nature")),
            S("puzzles", "Puzzles", "Пазлы|پہیلیاں|الألغاز", "puzzle",
                [N("Jigsaw Puzzle", 9, 49), N("3D Puzzle", 15, 79, "Pieces=72~160~250", "Finished size=20 × 20 × 25 cm~40 × 20 × 30 cm"), N("Brain Teaser Set", 7, 29, "Pieces=6~12", "Finished size=Pocket size")],
                7, 79,
                A("Pieces", "100|500|1,000|2,000"), A("Age", "5+|8+|12+|Adult"), A("Finished size", "50 × 35 cm|68 × 48 cm|98 × 69 cm")),
            S("outdoor-play", "Outdoor Play", "Игры на улице|بیرونی کھیل|الألعاب الخارجية", "outdoor-toy",
                [N("Scooter", 29, 149, "Material=Aluminium", "Age=3+~5+~8+", "Max weight=50 kg~100 kg"), N("Balance Bike", 39, 149, "Material=Birch plywood~Aluminium~Steel", "Age=2+~3+", "Max weight=25 kg~30 kg"),
                 N("Trampoline", 149, 599, "Material=Galvanised steel", "Age=5+~8+", "Max weight=100 kg~150 kg"), N("Swing Set", 99, 499, "Material=Galvanised steel~Pine", "Age=3+", "Max weight=50 kg~60 kg per seat")],
                29, 599,
                A("Age", "3+"), A("Max weight", "50 kg"), A("Material", "Aluminium")),
        ]),
        new("clothing", "Clothing", "Одежда|کپڑے|الملابس", "everyday wear", ["Thread & Oak", "Northline", "Merino Lane", "Coastal", "Everyday Supply"],
        [
            S("jackets-coats", "Jackets & Coats", "Куртки и пальто|جیکٹیں اور کوٹ|الجاكيتات والمعاطف", "jacket",
                [N("Rain Jacket", 59, 299, "Material=Recycled nylon", "Waterproof=10,000 mm~20,000 mm"), N("Down Jacket", 99, 499, "Material=Duck down~Goose down", "Waterproof=No~Water-repellent"),
                 N("Wool Coat", 149, 599, "Material=Merino wool~Wool-cashmere blend", "Waterproof=No"), N("Fleece Jacket", 39, 149, "Material=Recycled polyester fleece", "Waterproof=No"),
                 N("Softshell Jacket", 69, 249, "Material=Recycled nylon~Recycled polyester", "Waterproof=Water-repellent")],
                39, 599,
                A("Material", "Recycled nylon"), A("Fit", "Regular|Relaxed|Slim"), A("Sizes", "XS–XL|S–XXL|XS–3XL"), A("Colour", "Black|Navy|Olive|Stone|Rust"), A("Waterproof", "No")),
            S("tops-shirts", "Tops & Shirts", "Футболки и рубашки|شرٹس اور ٹاپس|القمصان والبلوزات", "top",
                [N("T-Shirt", 15, 49, "Material=Organic cotton~Cotton-modal blend"), N("Oxford Shirt", 39, 119, "Material=Organic cotton"), N("Linen Shirt", 49, 129, "Material=Linen"),
                 N("Polo Shirt", 29, 89, "Material=Organic cotton~Cotton piqué"), N("Merino Base Layer", 49, 149, "Material=Merino wool", "Fit=Slim")],
                15, 149,
                A("Material", "Organic cotton"), A("Fit", "Regular|Relaxed|Slim"), A("Sizes", "XS–XL|S–XXL|XS–3XL"), A("Colour", "White|Black|Navy|Sage|Oat")),
            S("trousers", "Trousers", "Брюки|پتلون|السراويل", "trousers",
                [N("Chinos", 39, 119, "Material=Stretch cotton~Organic cotton twill"), N("Jeans", 49, 199, "Material=Selvedge denim~Stretch denim"), N("Joggers", 29, 89, "Material=Cotton fleece~Recycled polyester", "Fit=Relaxed~Tapered"),
                 N("Hiking Trousers", 59, 159, "Material=Recycled polyester~Nylon ripstop"), N("Linen Trousers", 49, 129, "Material=Linen", "Fit=Relaxed~Straight")],
                29, 199,
                A("Material", "Stretch cotton"), A("Fit", "Slim|Straight|Relaxed|Tapered"), A("Waist", "26–36|28–38|30–40")),
            S("knitwear", "Knitwear", "Трикотаж|بنا ہوا لباس|الملابس المحبوكة", "knitwear", Ns("Crew-Neck Jumper|Cardigan|Roll-Neck Jumper|Knitted Vest"), 35, 299,
                A("Material", "Merino wool|Cashmere|Lambswool|Organic cotton"), A("Fit", "Regular|Relaxed|Slim"), A("Sizes", "XS–XL|S–XXL|XS–3XL")),
        ]),
        new("shoes", "Shoes", "Обувь|جوتے|الأحذية", "walking, running and everything between", ["Cobble & Co", "Fieldmark", "Pace", "Tread", "Arch & Sole"],
        [
            S("running-shoes", "Running Shoes", "Беговые кроссовки|دوڑنے کے جوتے|أحذية الجري", "running-shoe",
                [N("Road Running Shoe", 79, 199), N("Trail Running Shoe", 89, 219, "Weight=280 g~310 g"), N("Racing Shoe", 129, 279, "Cushioning=Light~Balanced", "Weight=180 g~210 g")], 59, 249,
                A("Cushioning", "Light|Balanced|Max"), A("Drop", "4 mm|6 mm|8 mm|10 mm"), A("Sizes", "EU 36–46|EU 38–48|EU 40–47"), A("Weight", "230 g|250 g|270 g")),
            S("boots", "Boots", "Ботинки|بوٹ|الأحذية الطويلة", "boot",
                [N("Hiking Boot", 99, 299, "Waterproof=Membrane lined", "Upper=Full-grain leather~Waterproof nylon"), N("Chelsea Boot", 89, 299, "Upper=Full-grain leather~Suede"),
                 N("Work Boot", 79, 249, "Upper=Full-grain leather", "Waterproof=No~Membrane lined"), N("Winter Boot", 89, 299, "Waterproof=Membrane lined", "Upper=Waterproof nylon~Full-grain leather")],
                69, 349,
                A("Upper", "Full-grain leather|Suede|Waterproof nylon"), A("Waterproof", "No"), A("Sizes", "EU 36–46|EU 38–48|EU 40–47")),
            S("sneakers", "Sneakers", "Кеды|اسنیکرز|الأحذية الرياضية الكاجوال", "sneaker",
                [N("Leather Sneaker", 79, 219, "Upper=Leather"), N("Canvas Sneaker", 39, 99, "Upper=Canvas"), N("Knit Sneaker", 69, 159, "Upper=Recycled knit"), N("Suede Sneaker", 79, 199, "Upper=Suede")], 39, 219,
                A("Upper", "Leather"), A("Sole", "Rubber cupsole|Natural rubber|EVA foam"), A("Sizes", "EU 36–46|EU 38–48|EU 40–47")),
            S("sandals-slippers", "Sandals & Slippers", "Сандалии и тапочки|سینڈل اور چپل|الصنادل والشباشب", "sandal",
                [N("Sandal", 39, 149, "Material=Cork and leather~Leather"), N("Slide", 15, 79, "Material=EVA~Cork and leather"), N("Slipper", 25, 99, "Material=Wool felt~Suede~Sheepskin")], 15, 149,
                A("Material", "EVA"), A("Sizes", "EU 36–46|EU 38–48|EU 40–47"), A("Colour", "Black|Tan|Grey|Olive")),
        ]),
        new("beauty", "Beauty & Personal Care", "Красота и уход|خوبصورتی اور ذاتی نگہداشت|الجمال والعناية الشخصية", "daily care routines", ["Pure Botanica", "Clearday", "Solace", "Veritas Skin", "Morrow"],
        [
            S("skincare", "Skincare", "Уход за кожей|جلد کی نگہداشت|العناية بالبشرة", "skincare",
                [N("Moisturiser", 12, 79, "Size=50 ml~100 ml", "Key ingredient=Hyaluronic acid~Ceramides~Niacinamide"), N("Serum", 15, 129, "Size=30 ml", "Key ingredient=Hyaluronic acid~Niacinamide~Vitamin C~Retinol"),
                 N("Cleanser", 7, 39, "Size=150 ml~200 ml", "Key ingredient=Ceramides~Niacinamide~Salicylic acid"), N("Sunscreen", 9, 49, "Size=50 ml~100 ml", "Key ingredient=SPF 30~SPF 50"),
                 N("Night Cream", 19, 99, "Size=50 ml", "Key ingredient=Retinol~Ceramides~Peptides")],
                7, 129,
                A("Size", "50 ml"), A("Skin type", "All|Dry|Oily|Sensitive|Combination"), A("Key ingredient", "Hyaluronic acid")),
            S("hair-care", "Hair Care", "Уход за волосами|بالوں کی نگہداشت|العناية بالشعر", "hair-care",
                [N("Hair Dryer", 29, 449, "Power=1,600 W~1,800 W~2,000 W", "Feature=Ionic~Cool shot"), N("Hair Straightener", 25, 249, "Power=45 W~60 W", "Feature=Ceramic plates~Titanium plates"),
                 N("Curling Wand", 25, 179, "Power=45 W~60 W", "Feature=Ceramic barrel~Heat protection"), N("Hot Air Brush", 39, 399, "Power=1,000 W~1,300 W", "Feature=Ionic~Heat protection")],
                19, 449,
                A("Power", "1,800 W"), A("Hair type", "All|Fine|Thick|Curly"), A("Feature", "Ionic")),
            S("shaving-grooming", "Shaving & Grooming", "Бритьё и груминг|شیونگ اور گرومنگ|الحلاقة والعناية", "grooming",
                [N("Electric Shaver", 49, 349, "Attachments=1~2", "Runtime=45 minutes~60 minutes"), N("Beard Trimmer", 19, 129, "Attachments=4~8~12", "Runtime=60 minutes~90 minutes"),
                 N("Safety Razor", 19, 79, "Runtime=Manual", "Wet and dry=Wet only", "Attachments=1"), N("Grooming Kit", 29, 149, "Attachments=8~12", "Runtime=60 minutes~90 minutes")],
                12, 349,
                A("Runtime", "60 minutes"), A("Wet and dry", "Yes|No"), A("Attachments", "4")),
            S("fragrance", "Fragrance", "Парфюмерия|خوشبو|العطور", "fragrance",
                [N("Eau de Parfum", 39, 199, "Concentration=Eau de parfum", "Size=30 ml~50 ml~100 ml"), N("Eau de Toilette", 25, 129, "Concentration=Eau de toilette", "Size=50 ml~100 ml"),
                 N("Body Mist", 12, 39, "Concentration=Body mist", "Size=100 ml~250 ml"), N("Solid Perfume", 15, 49, "Concentration=Solid", "Size=10 g~15 g")],
                12, 199,
                A("Size", "50 ml"), A("Scent family", "Floral|Woody|Citrus|Amber|Fresh"), A("Concentration", "Eau de parfum")),
        ]),
        new("health", "Health", "Здоровье|صحت|الصحة", "looking after yourself at home", ["Vitalis", "Carewell", "Pulse Health", "Medica Home", "Wellspring"],
        [
            S("health-monitors", "Health Monitors", "Медицинские приборы|صحت کے مانیٹر|أجهزة مراقبة الصحة", "health-monitor",
                [N("Blood Pressure Monitor", 29, 149, "Measurement=Upper arm~Wrist"), N("Pulse Oximeter", 15, 59, "Measurement=Fingertip", "Memory=None~App sync"),
                 N("Digital Thermometer", 9, 59, "Measurement=Forehead and ear~Oral and underarm", "Memory=None~10 readings"), N("Smart Scale", 29, 149, "Measurement=Body composition", "Memory=App sync", "Power=AAA batteries~USB-C rechargeable")],
                9, 199,
                A("Measurement", "Upper arm"), A("Memory", "60 readings|2 users × 120 readings|App sync"), A("Power", "AAA batteries|USB-C rechargeable|AA batteries")),
            S("mobility-support", "Mobility & Support", "Поддержка и мобильность|نقل و حرکت اور سہارا|الحركة والدعم", "support",
                [N("Knee Support", 12, 59), N("Walking Stick", 15, 79, "Material=Aluminium~Carbon fibre", "Size=Adjustable 75–97 cm", "Side=Universal"), N("Back Brace", 19, 89, "Side=Universal"), N("Wrist Splint", 9, 39)],
                9, 149,
                A("Size", "S|M|L"), A("Material", "Neoprene|Breathable knit"), A("Side", "Left|Right|Universal")),
            S("massage-recovery", "Massage & Recovery", "Массаж и восстановление|مساج اور بحالی|التدليك والاستشفاء", "massage",
                [N("Massage Gun", 59, 349, "Intensity=5 levels~20 levels", "Battery=3 hours~6 hours", "Attachments=4~6"), N("Foam Roller", 15, 69, "Intensity=Medium~Firm", "Battery=None", "Attachments=None"),
                 N("Heating Pad", 19, 79, "Intensity=3 levels~6 levels", "Battery=Mains", "Attachments=None"), N("Neck Massager", 29, 149, "Intensity=3 levels~5 levels", "Battery=2 hours~4 hours", "Attachments=None")],
                15, 349,
                A("Intensity", "5 levels"), A("Battery", "Mains"), A("Attachments", "None")),
            S("first-aid", "First Aid", "Аптечки|ابتدائی طبی امداد|الإسعافات الأولية", "first-aid", Ns("First Aid Kit|Travel First Aid Kit|Burns Kit"), 7, 99,
                A("Pieces", "42|100|160|220"), A("Case", "Soft pouch|Hard case|Wall-mounted box"), A("Use", "Home|Travel|Car|Workplace")),
        ]),
        new("pet-supplies", "Pet Supplies", "Товары для животных|پالتو جانوروں کا سامان|مستلزمات الحيوانات الأليفة", "pets and the people who look after them", ["Wagtail", "Whisker & Co", "Burrow", "Fetch Supply", "Tailwind Pet"],
        [
            S("dog", "Dog", "Собаки|کتے|الكلاب", "dog",
                [N("Dog Bed", 29, 199, "Material=Memory foam~Recycled polyester"), N("Dog Harness", 15, 69, "Material=Nylon webbing~Padded mesh"), N("Dog Lead", 9, 49, "Material=Nylon webbing~Leather"),
                 N("Chew Toy", 5, 25, "Material=Natural rubber~Nylon")],
                5, 199,
                A("Size", "XS|S|M|L|XL"), A("Suitable for", "Puppies|Adult dogs|Senior dogs|All ages"), A("Material", "Nylon webbing")),
            S("cat", "Cat", "Кошки|بلیاں|القطط", "cat",
                [N("Scratching Post", 19, 99, "Material=Sisal"), N("Cat Litter", 7, 29, "Material=Clumping clay~Recycled paper~Tofu", "Size=5 kg~10 kg"), N("Cat Bed", 19, 89, "Material=Plush fleece~Wool felt"),
                 N("Cat Tree", 49, 299, "Material=Sisal and plush", "Size=100 cm~150 cm~180 cm")],
                7, 299,
                A("Size", "S|M|L"), A("Suitable for", "Kittens|Adult cats|Senior cats|All ages"), A("Material", "Sisal")),
            S("aquarium", "Aquarium", "Аквариумистика|ایکویریم|أحواض الأسماك", "aquarium",
                [N("Aquarium Kit", 49, 599, "Power=15 W~40 W"), N("Aquarium Filter", 19, 199, "Power=5 W~15 W~25 W"), N("Aquarium Heater", 15, 69, "Power=25 W~100 W~200 W"),
                 N("LED Aquarium Light", 19, 149, "Power=10 W~25 W~40 W")],
                12, 599,
                A("Capacity", "Up to 20 L|Up to 54 L|Up to 120 L|Up to 240 L"), A("Power", "25 W"), A("Type", "Freshwater|Marine|Freshwater and marine")),
            S("small-pets-birds", "Small Pets & Birds", "Грызуны и птицы|چھوٹے جانور اور پرندے|الحيوانات الصغيرة والطيور", "small-pet",
                [N("Hamster Cage", 29, 129, "Suitable for=Hamsters~Gerbils", "Dimensions=60 × 40 × 40 cm~80 × 50 × 50 cm"), N("Rabbit Hutch", 99, 399, "Suitable for=Rabbits~Guinea pigs", "Material=Fir wood", "Dimensions=150 × 60 × 90 cm"),
                 N("Bird Cage", 39, 249, "Suitable for=Budgies~Canaries", "Material=Powder-coated steel", "Dimensions=50 × 30 × 70 cm~80 × 50 × 150 cm"),
                 N("Bird Feeder", 9, 49, "Suitable for=Wild birds", "Material=Recycled plastic~Fir wood~Powder-coated steel", "Dimensions=20 × 20 × 30 cm")],
                9, 399,
                A("Suitable for", "Hamsters"), A("Material", "Powder-coated steel|Fir wood|Recycled plastic"), A("Dimensions", "80 × 50 × 50 cm")),
        ]),
        new("grocery", "Grocery", "Продукты|گروسری|البقالة", "the kitchen cupboard", ["Harvest Row", "Stoneground", "Copper Kettle", "Olive & Salt", "Morning Field"],
        [
            S("coffee-beans", "Coffee", "Кофе|کافی|القهوة", "coffee-beans",
                [N("Whole Bean Coffee"), N("Ground Coffee"), N("Espresso Blend", "Origin=Blend", "Roast=Medium-dark~Dark"), N("Single-Origin Coffee", 12, 49, "Origin=Ethiopia~Colombia~Guatemala~Kenya", "Roast=Light~Medium")],
                6, 39,
                A("Weight", "250 g|500 g|1 kg"), A("Roast", "Light|Medium|Medium-dark|Dark"), A("Origin", "Brazil|Colombia|Blend")),
            S("tea", "Tea", "Чай|چائے|الشاي", "tea",
                [N("Loose Leaf Tea", "Weight=100 g~250 g"), N("Tea Bags", 3, 12, "Weight=40 bags~80 bags"), N("Matcha", 12, 39, "Type=Green", "Origin=Japan", "Weight=30 g~50 g~100 g"),
                 N("Herbal Infusion", 4, 15, "Type=Herbal", "Origin=Egypt~South Africa~Greece", "Weight=40 bags~100 g")],
                4, 39,
                A("Weight", "100 g"), A("Type", "Black|Green|Oolong|White"), A("Origin", "Assam|Darjeeling|Yunnan|Kenya|Taiwan")),
            S("snacks", "Snacks", "Снеки|اسنیکس|الوجبات الخفيفة", "snack", Ns("Mixed Nuts|Dark Chocolate|Granola|Crackers|Dried Fruit"), 2, 35,
                A("Weight", "100 g|200 g|500 g|1 kg"), A("Dietary", "Vegan|Gluten-free|No added sugar|Organic"), A("Pack", "1|3|6|12")),
            S("pantry", "Pantry", "Бакалея|پینٹری|المؤن", "pantry",
                [N("Extra Virgin Olive Oil", 8, 39, "Size=500 ml~1 L", "Origin=Italy~Spain~Greece"), N("Basmati Rice", 4, 29, "Size=1 kg~5 kg", "Origin=Pakistan~India"),
                 N("Pasta", 2, 9, "Size=500 g~1 kg", "Origin=Italy"), N("Honey", 6, 39, "Size=250 g~500 g", "Origin=Greece~Spain~Pakistan~New Zealand"), N("Spice Set", 12, 59, "Size=6 jars~12 jars", "Origin=India~Pakistan")],
                2, 59,
                A("Size", "500 g"), A("Origin", "Italy"), A("Dietary", "Organic|Vegan|Gluten-free|Halal")),
        ]),
        new("musical-instruments", "Musical Instruments", "Музыкальные инструменты|موسیقی کے آلات|الآلات الموسيقية", "practice, performance and recording", ["Fretwork", "Tonewood", "Cadence", "Harmonia", "Resonant"],
        [
            S("guitars", "Guitars", "Гитары|گٹار|الغيتارات", "guitar",
                [N("Acoustic Guitar", 149, 1999, "Body=Solid spruce top~Cedar top~Mahogany", "Pickups=None~Piezo", "Scale length=25.5 in~24.75 in"),
                 N("Electric Guitar", 199, 2499, "Body=Alder~Ash~Mahogany", "Pickups=Single-coil~Humbucker", "Scale length=25.5 in~24.75 in"),
                 N("Classical Guitar", 99, 1299, "Body=Cedar top~Solid spruce top", "Pickups=None~Piezo", "Scale length=650 mm"),
                 N("Bass Guitar", 199, 1999, "Body=Alder~Ash", "Pickups=Single-coil~Humbucker", "Scale length=34 in")],
                99, 2499,
                A("Body", "Mahogany"), A("Scale length", "25.5 in"), A("Pickups", "None")),
            S("keyboards-pianos", "Keyboards & Pianos", "Клавишные и пианино|کی بورڈ اور پیانو|لوحات المفاتيح والبيانو", "piano",
                [N("Digital Piano", 399, 2999, "Keys=88", "Action=Weighted hammer", "Polyphony=128 notes~256 notes"), N("Stage Piano", 599, 2499, "Keys=76~88", "Action=Weighted hammer~Semi-weighted"),
                 N("Portable Keyboard", 99, 499, "Keys=61~76", "Action=Synth action~Touch-sensitive", "Polyphony=48 notes~64 notes"),
                 N("MIDI Controller", 59, 599, "Keys=25~49~61", "Action=Synth action~Semi-weighted", "Polyphony=Not applicable")],
                59, 2999,
                A("Keys", "88"), A("Action", "Weighted hammer"), A("Polyphony", "128 notes")),
            S("drums-percussion", "Drums & Percussion", "Ударные|ڈرم اور پرکشن|الطبول والإيقاع", "drums",
                [N("Electronic Drum Kit", 349, 1999, "Shell=Mesh pads~Rubber pads", "Pieces=5~7", "Cymbals=3"), N("Acoustic Drum Kit", 399, 1999, "Shell=Birch~Maple~Poplar", "Pieces=5~7", "Cymbals=3~4"),
                 N("Cajón", 59, 299, "Pieces=1", "Shell=Birch plywood", "Cymbals=None"), N("Snare Drum", 99, 599, "Pieces=1", "Shell=Maple~Birch~Steel", "Cymbals=None")],
                59, 1999,
                A("Pieces", "5"), A("Shell", "Birch"), A("Cymbals", "3")),
            S("studio-recording", "Studio & Recording", "Студийное оборудование|اسٹوڈیو اور ریکارڈنگ|الاستوديو والتسجيل", "studio",
                [N("Audio Interface", 79, 1499, "Inputs=2~4~8"), N("Digital Mixer", 299, 1499, "Inputs=12~16~24", "Connection=USB-C~USB-C and Ethernet"),
                 N("Portable Recorder", 99, 499, "Inputs=2~4", "Connection=USB-C", "Resolution=24-bit / 96 kHz~32-bit float / 192 kHz")],
                79, 1499,
                A("Inputs", "2"), A("Resolution", "24-bit / 96 kHz|24-bit / 192 kHz|32-bit / 192 kHz"), A("Connection", "USB-C|Thunderbolt|USB-C and ADAT")),
        ]),
        new("photography", "Photography", "Фото|فوٹوگرافی|التصوير", "stills and video", ["Aperture Works", "Lumen Optics", "Shutterline", "Halide Imaging", "Focal Point"],
        [
            S("cameras", "Cameras", "Фотоаппараты|کیمرے|الكاميرات", "camera",
                [N("Mirrorless Camera", 599, 3999, "Sensor=Micro Four Thirds~APS-C~Full frame", "Resolution=24 MP~33 MP~45 MP~61 MP", "Stabilisation=None~5-axis in-body"),
                 N("Compact Camera", 399, 1499, "Sensor=1 in~Micro Four Thirds", "Resolution=20 MP~24 MP", "Stabilisation=Optical"),
                 N("Action Camera", 149, 549, "Sensor=1/1.9 in~1 in", "Resolution=12 MP~27 MP", "Video=4K 60~5.3K 60", "Stabilisation=Electronic"),
                 N("Vlogging Camera", 449, 1199, "Sensor=1 in~APS-C", "Resolution=20 MP~24 MP", "Video=4K 30~4K 60", "Stabilisation=Optical~Electronic")],
                149, 3999,
                A("Sensor", "APS-C"), A("Resolution", "24 MP"), A("Video", "4K 30|4K 60|6K 30|8K 30"), A("Stabilisation", "5-axis in-body")),
            S("lenses", "Lenses", "Объективы|لینز|العدسات", "lens",
                [N("Prime Lens", 199, 1999, "Focal length=24 mm~35 mm~50 mm~85 mm", "Aperture=f/1.2~f/1.4~f/1.8"), N("Zoom Lens", 399, 2499, "Focal length=24–70 mm~24–105 mm~70–200 mm", "Aperture=f/2.8~f/4"),
                 N("Macro Lens", 299, 1299, "Focal length=90 mm~100 mm", "Aperture=f/2.8"), N("Telephoto Lens", 599, 2499, "Focal length=70–200 mm~100–400 mm~150–600 mm", "Aperture=f/2.8~f/4.5–5.6~f/5–6.3")],
                149, 2499,
                A("Focal length", "50 mm"), A("Aperture", "f/1.8"), A("Mount", "E-mount|RF-mount|Z-mount|X-mount")),
            S("tripods-supports", "Tripods & Supports", "Штативы|ٹرائی پوڈ|الحوامل", "tripod",
                [N("Travel Tripod", 79, 499, "Max height=1.5 m~1.65 m"), N("Video Tripod", 149, 799, "Max load=8 kg~12 kg", "Max height=1.65 m~1.8 m"),
                 N("Gimbal", 99, 699, "Max load=1.2 kg~3 kg~4.5 kg", "Max height=Handheld", "Material=Aluminium~Carbon fibre and aluminium"), N("Monopod", 25, 199, "Max load=5 kg~12 kg", "Max height=1.6 m~1.8 m")],
                25, 799,
                A("Max load", "5 kg|8 kg"), A("Max height", "1.5 m"), A("Material", "Aluminium|Carbon fibre")),
            S("studio-lighting", "Studio Lighting", "Студийный свет|اسٹوڈیو لائٹنگ|إضاءة الاستوديو", "studio-light",
                [N("LED Panel", 49, 399, "Output=20 W~60 W"), N("Ring Light", 19, 149, "Output=10 W~18 W", "Power=USB-C~Mains"), N("Softbox Kit", 79, 399, "Output=85 W~150 W", "Power=Mains"),
                 N("COB Video Light", 149, 899, "Output=150 W~300 W~600 W", "Power=Mains~V-mount battery")],
                19, 899,
                A("Output", "60 W"), A("Colour temperature", "5600 K|3200–5600 K|2700–6500 K and RGB"), A("Power", "USB-C|NP-F battery|Mains")),
        ]),
        new("smart-home", "Smart Home", "Умный дом|اسمارٹ ہوم|المنزل الذكي", "a home that runs itself", ["Nestwise", "Homelink", "Beacon", "Luma Home", "Hearthsense"],
        [
            S("security-cameras", "Security Cameras", "Камеры видеонаблюдения|سیکیورٹی کیمرے|كاميرات المراقبة", "security-camera",
                [N("Indoor Camera", 29, 149, "Power=Mains~USB-C"), N("Outdoor Camera", 59, 299), N("Video Doorbell", 49, 249, "Power=Battery~Wired doorbell"), N("Floodlight Camera", 149, 399, "Power=Mains")],
                29, 399,
                A("Resolution", "1080p|2K|4K"), A("Power", "Mains|Battery|Solar|PoE"), A("Storage", "Cloud|microSD|Cloud and microSD|Local hub")),
            S("smart-lighting", "Smart Lighting", "Умное освещение|اسمارٹ لائٹنگ|الإضاءة الذكية", "smart-light",
                [N("Smart Bulb", 9, 59, "Fitting=E27~E14~GU10"), N("Smart Light Strip", 29, 149, "Fitting=Strip, 2 m~Strip, 5 m"), N("Smart Bulb Starter Kit", 59, 249, "Fitting=E27~GU10")], 9, 249,
                A("Fitting", "E27"), A("Colour", "White|Tunable white|Full colour"), A("Protocol", "Wi-Fi|Zigbee|Thread / Matter|Bluetooth")),
            S("climate-control", "Climate Control", "Климат-контроль|موسمی کنٹرول|التحكم بالمناخ", "climate",
                [N("Smart Thermostat", 99, 349, "Power=Mains~Battery", "Compatibility=Gas boilers~Heat pumps~All systems"), N("Smart Radiator Valve", 39, 89, "Power=Battery", "Compatibility=Wet radiators"),
                 N("Air Quality Monitor", 49, 249, "Power=USB-C", "Compatibility=Any room"), N("Smart Fan", 79, 349, "Power=Mains", "Compatibility=Any room")],
                19, 349,
                A("Protocol", "Wi-Fi|Zigbee|Thread / Matter"), A("Power", "Mains"), A("Compatibility", "All systems")),
            S("plugs-hubs", "Plugs & Hubs", "Розетки и хабы|پلگ اور ہب|المقابس والمحاور", "smart-plug",
                [N("Smart Plug", 9, 39), N("Smart Hub", 49, 199, "Max load=Not applicable", "Energy monitoring=No"), N("Smart Power Strip", 29, 99), N("Smart Switch", 19, 79)], 9, 199,
                A("Protocol", "Wi-Fi|Zigbee|Thread / Matter"), A("Max load", "10 A|13 A|16 A"), A("Energy monitoring", "Yes|No")),
        ]),
    ];
}
