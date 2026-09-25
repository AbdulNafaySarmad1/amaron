using Commerce.Contracts;
using Commerce.Domain;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Commerce.Application;

public sealed partial class CatalogService(ICommerceDbContext db, IReadModelCache cache, IProductSearch search)
{
    public const string CanonicalLocale = "en";

    /// <summary>Language of requested catalog text: "ur-PK" -> "ur". Anything malformed means the canonical language.</summary>
    public static string ContentLocale(string? locale)
    {
        var language = locale?.Trim().ToLowerInvariant().Split('-')[0];
        return language is not null && LanguageTag().IsMatch(language) ? language : CanonicalLocale;
    }

    [GeneratedRegex("^[a-z]{2,3}$")]
    private static partial Regex LanguageTag();

    /// <summary>Category names in the requested language, falling back to the canonical name per category.</summary>
    public async Task<IReadOnlyList<CategoryDto>> GetCategoriesAsync(string? locale, CancellationToken cancellationToken)
    {
        var loc = ContentLocale(locale);
        return await cache.GetOrCreateAsync(
            $"categories:v2:{loc}",
            async token => await db.Categories.AsNoTracking().OrderBy(x => x.SortOrder).ThenBy(x => x.Name)
                .Select(x => new CategoryDto(x.Id, x.Slug,
                    x.Translations.Where(t => t.Locale == loc).Select(t => t.Name).FirstOrDefault() ?? x.Name,
                    x.ParentId,
                    x.Translations.Any(t => t.Locale == loc) ? loc : CanonicalLocale)).ToListAsync(token),
            TimeSpan.FromMinutes(10), ["categories", "homepage"], cancellationToken);
    }

    public async Task<ProductDetailDto> GetProductAsync(string slug, string? locale, CancellationToken cancellationToken)
    {
        var loc = ContentLocale(locale);
        return await cache.GetOrCreateAsync(
            $"product:v2:{slug.ToLowerInvariant()}:{loc}",
            async token => await LoadProductAsync(slug, loc, token) ?? throw CommerceErrors.NotFound("Product"),
            TimeSpan.FromMinutes(5), ["products", $"product:{slug.ToLowerInvariant()}"], cancellationToken);
    }

    public async Task<ProductPageDto> SearchAsync(SearchRequest request, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        using var activity = CommerceTelemetry.ActivitySource.StartActivity("catalog.search");
        try
        {
            ValidateSearch(request);
            var loc = ContentLocale(request.Locale);
            var query = db.Products.AsNoTracking().Where(p => p.Status == ProductStatus.Active && p.Variants.Any(v => v.IsActive && v.Inventory != null));
            var matches = string.IsNullOrWhiteSpace(request.Query) ? null : search.Match(request.Query, loc);
            if (matches is not null) query = query.Join(matches, p => p.Id, m => m.ProductId, (p, _) => p);
            if (!string.IsNullOrWhiteSpace(request.Category))
            {
                var scope = CategoryScope(await GetCategoriesAsync(CanonicalLocale, cancellationToken), request.Category);
                query = query.Where(p => scope.Contains(p.CategoryId));
            }
            if (request.MinPrice is not null || request.MaxPrice is not null)
                query = query.Where(p => p.Variants.Any(v => v.IsActive && v.Inventory != null &&
                    (request.MinPrice == null || v.Price >= request.MinPrice) && (request.MaxPrice == null || v.Price <= request.MaxPrice)));
            if (request.MinimumRating is not null) query = query.Where(p => p.Reviews.Where(r => r.IsApproved).Average(r => (decimal?)r.Rating) >= request.MinimumRating);
            var sellable = SellableStock.Query(db);
            if (request.Available == true) query = query.Where(p => p.Variants.Any(v => v.IsActive && sellable.Any(s => s.VariantId == v.Id && s.Units > 0)));

            // Brand counts ignore the brand filter itself, so choosing one brand still shows the alternatives.
            var brandRows = await query.GroupBy(p => p.Brand).Select(g => new { Value = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count).ThenBy(x => x.Value).Take(20).ToListAsync(cancellationToken);
            var brands = brandRows.Select(x => new FacetValueDto(x.Value, x.Count)).ToList();
            if (!string.IsNullOrWhiteSpace(request.Brand)) query = query.Where(p => p.Brand == request.Brand);

            query = request.Sort?.ToLowerInvariant() switch
            {
                "price-asc" => query.OrderBy(p => p.Variants.Where(v => v.IsActive).Min(v => v.Price)).ThenBy(p => p.Id),
                "price-desc" => query.OrderByDescending(p => p.Variants.Where(v => v.IsActive).Min(v => v.Price)).ThenBy(p => p.Id),
                "rating" => query.OrderByDescending(p => p.Reviews.Where(r => r.IsApproved).Average(r => (decimal?)r.Rating) ?? 0).ThenBy(p => p.Id),
                "newest" => query.OrderByDescending(p => p.CreatedAt).ThenBy(p => p.Id),
                "name" => query.OrderBy(p => p.Title).ThenBy(p => p.Id),
                // With a query, the default order is relevance; the rank comes from a join, never a per-row subquery.
                _ when matches is not null => query.Join(matches, p => p.Id, m => m.ProductId, (p, m) => new { p, m.Rank }).OrderByDescending(x => x.Rank).ThenBy(x => x.p.Id).Select(x => x.p),
                _ => query.OrderBy(p => p.Title).ThenBy(p => p.Id)
            };

            var total = await query.CountAsync(cancellationToken);
            var rows = await ProjectCards(query.Skip((request.Page - 1) * request.PageSize).Take(request.PageSize), loc).ToListAsync(cancellationToken);
            activity?.SetTag("commerce.search.results", total);
            return new ProductPageDto(rows.Select(MapCard).ToList(), request.Page, request.PageSize, total, (int)Math.Ceiling((double)total / request.PageSize), brands);
        }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            activity?.SetTag("error.type", exception.GetType().FullName);
            throw;
        }
        finally
        {
            CommerceTelemetry.SearchDuration.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
    }

    /// <summary>Uses the same full-text and typo-tolerant matching as search ("headphnes" finds headphones), including
    /// translated names, names starting with the term first. With a category, only that subtree is suggested.</summary>
    public async Task<IReadOnlyList<SuggestionDto>> SuggestAsync(string query, string? category, string? locale, CancellationToken cancellationToken)
    {
        query = query.Trim();
        if (query.Length < 2) return [];
        if (query.Length > 80) throw CommerceErrors.Validation("The suggestion query cannot exceed 80 characters.");
        category = string.IsNullOrWhiteSpace(category) ? null : category.Trim().ToLowerInvariant();
        var loc = ContentLocale(locale);
        return await cache.GetOrCreateAsync(
            $"suggest:v4:{loc}:{category}:{query.ToLowerInvariant()}",
            async token =>
            {
                var normalized = query.ToLower();
                var products = db.Products.AsNoTracking().Where(p => p.Status == ProductStatus.Active);
                var categories = db.Categories.AsNoTracking().Where(c => c.Name.ToLower().Contains(normalized) || c.Translations.Any(t => t.Locale == loc && t.Name.ToLower().Contains(normalized)));
                if (category is not null)
                {
                    var scope = CategoryScope(await GetCategoriesAsync(CanonicalLocale, token), category);
                    products = products.Where(p => scope.Contains(p.CategoryId));
                    categories = categories.Where(c => scope.Contains(c.Id));
                }
                var productRows = await products.Join(search.Match(query, loc), p => p.Id, m => m.ProductId, (p, m) => new
                    {
                        Title = p.Translations.Where(t => t.Locale == loc).Select(t => t.Title).FirstOrDefault() ?? p.Title,
                        p.Slug,
                        m.Rank,
                        Locale = p.Translations.Any(t => t.Locale == loc) ? loc : CanonicalLocale,
                    })
                    .OrderByDescending(x => x.Title.ToLower().StartsWith(normalized)).ThenByDescending(x => x.Rank).ThenBy(x => x.Title).Take(8)
                    .Select(x => new SuggestionDto("product", x.Title, x.Slug, x.Locale)).ToListAsync(token);
                var categoryRows = await categories.Select(c => new { Name = c.Translations.Where(t => t.Locale == loc).Select(t => t.Name).FirstOrDefault() ?? c.Name, c.Slug, Locale = c.Translations.Any(t => t.Locale == loc) ? loc : CanonicalLocale })
                    .OrderByDescending(c => c.Name.ToLower().StartsWith(normalized)).ThenBy(c => c.Name).Take(4)
                    .Select(c => new SuggestionDto("category", c.Name, c.Slug, c.Locale)).ToListAsync(token);
                return productRows.Concat(categoryRows).Take(10).ToList();
            }, TimeSpan.FromMinutes(1), ["search-suggestions"], cancellationToken);
    }

    public async Task<IReadOnlyList<ProductCardDto>> GetBatchAsync(IReadOnlyList<Guid> ids, string? locale, CancellationToken cancellationToken)
    {
        if (ids.Count is < 1 or > 50 || ids.Distinct().Count() != ids.Count) throw CommerceErrors.Validation("Provide between 1 and 50 unique product IDs.");
        var rows = await ProjectCards(db.Products.AsNoTracking().Where(p => ids.Contains(p.Id) && p.Status == ProductStatus.Active && p.Variants.Any(v => v.IsActive && v.Inventory != null)), ContentLocale(locale)).ToListAsync(cancellationToken);
        var byId = rows.Select(MapCard).ToDictionary(x => x.Id);
        return ids.Where(byId.ContainsKey).Select(id => byId[id]).ToList();
    }

    public async Task<IReadOnlyList<ProductCardDto>> GetFeaturedAsync(int count, string? locale, CancellationToken cancellationToken)
    {
        var rows = await ProjectCards(db.Products.AsNoTracking().Where(p => p.Status == ProductStatus.Active && p.IsFeatured && p.Variants.Any(v => v.IsActive && v.Inventory != null)).OrderBy(p => p.Title).Take(Math.Clamp(count, 1, 24)), ContentLocale(locale)).ToListAsync(cancellationToken);
        return rows.Select(MapCard).ToList();
    }

    public async Task<IReadOnlyList<ProductCardDto>> GetRelatedAsync(Guid productId, int count, string? locale, CancellationToken cancellationToken)
    {
        // Keyed on the category ID: names are display text and are not unique across parents.
        var categoryId = db.Products.Where(x => x.Id == productId).Select(x => x.CategoryId);
        var rows = await ProjectCards(db.Products.AsNoTracking().Where(p => p.Status == ProductStatus.Active && p.Id != productId && categoryId.Contains(p.CategoryId) && p.Variants.Any(v => v.IsActive && v.Inventory != null)).OrderByDescending(p => p.IsFeatured).ThenBy(p => p.Id).Take(Math.Clamp(count, 1, 12)), ContentLocale(locale)).ToListAsync(cancellationToken);
        return rows.Select(MapCard).ToList();
    }

    private static readonly RelationshipType[] SetupTypes = [RelationshipType.Accessory, RelationshipType.Compatible, RelationshipType.Complementary];

    /// <summary>Curated relationships for a product page, grouped by type in merchandising order, each with its reason.</summary>
    public async Task<IReadOnlyList<RelationshipGroupDto>> GetRelationshipsAsync(Guid productId, string? locale, CancellationToken cancellationToken)
    {
        var links = await db.ProductRelationships.AsNoTracking().Where(r => r.SourceProductId == productId && r.Target.Status == ProductStatus.Active)
            .OrderByDescending(r => r.RelevanceScore).ThenBy(r => r.TargetProductId).Select(r => new { r.TargetProductId, r.Type, r.Reason }).ToListAsync(cancellationToken);
        if (links.Count == 0) return [];
        var ids = links.Select(l => l.TargetProductId).Distinct().ToList();
        var cards = (await ProjectCards(db.Products.AsNoTracking().Where(p => ids.Contains(p.Id) && p.Variants.Any(v => v.IsActive && v.Inventory != null)), ContentLocale(locale)).ToListAsync(cancellationToken))
            .Select(MapCard).ToDictionary(c => c.Id);
        return links.Where(l => cards.ContainsKey(l.TargetProductId)).GroupBy(l => l.Type).OrderBy(g => g.Key)
            .Select(g => new RelationshipGroupDto(TypeKey(g.Key), g.Take(4).Select(l => new RelatedProductDto(cards[l.TargetProductId], l.Reason)).ToList())).ToList();
    }

    /// <summary>At most one setup item for what is already in the bag: sellable now, not already there, highest relevance first.</summary>
    public async Task<RelatedProductDto?> GetUsefulAdditionAsync(IReadOnlyList<Guid> productIds, string? locale, CancellationToken cancellationToken)
    {
        if (productIds.Count is < 1 or > 50 || productIds.Distinct().Count() != productIds.Count) throw CommerceErrors.Validation("Provide between 1 and 50 unique product IDs.");
        var sellable = SellableStock.Query(db);
        var pick = await db.ProductRelationships.AsNoTracking()
            .Where(r => productIds.Contains(r.SourceProductId) && !productIds.Contains(r.TargetProductId) && SetupTypes.Contains(r.Type)
                && r.Target.Status == ProductStatus.Active && r.Target.Variants.Any(v => v.IsActive && sellable.Any(s => s.VariantId == v.Id && s.Units > 0)))
            .OrderByDescending(r => r.RelevanceScore).ThenBy(r => r.TargetProductId).Select(r => new { r.TargetProductId, r.Reason }).FirstOrDefaultAsync(cancellationToken);
        if (pick is null) return null;
        var card = await ProjectCards(db.Products.AsNoTracking().Where(p => p.Id == pick.TargetProductId), ContentLocale(locale)).SingleAsync(cancellationToken);
        return new RelatedProductDto(MapCard(card), pick.Reason);
    }

    /// <summary>"FrequentlyBoughtWith" -> "frequentlyBoughtWith": stable keys the storefront maps to headings.</summary>
    public static string TypeKey(RelationshipType type) { var name = type.ToString(); return char.ToLowerInvariant(name[0]) + name[1..]; }

    /// <summary>The category and all of its descendants. An unknown slug yields an empty scope, never "all products".</summary>
    public static IReadOnlyList<Guid> CategoryScope(IReadOnlyList<CategoryDto> categories, string slug)
    {
        var root = categories.FirstOrDefault(c => string.Equals(c.Slug, slug, StringComparison.OrdinalIgnoreCase));
        if (root is null) return [];
        var children = categories.Where(c => c.ParentId is not null).ToLookup(c => c.ParentId!.Value);
        var scope = new HashSet<Guid> { root.Id };
        var pending = new Queue<Guid>([root.Id]);
        while (pending.TryDequeue(out var id))
            foreach (var child in children[id])
                if (scope.Add(child.Id)) pending.Enqueue(child.Id);
        return scope.ToList();
    }

    private async Task<ProductDetailDto?> LoadProductAsync(string slug, string loc, CancellationToken token)
    {
        var sellable = SellableStock.Query(db);
        var product = await db.Products.AsNoTracking().Where(p => p.Slug == slug && p.Status == ProductStatus.Active)
            .Select(p => new
            {
                p.Id,
                p.Slug,
                p.Title,
                p.Brand,
                p.Description,
                Translation = p.Translations.Where(t => t.Locale == loc).Select(t => new { t.Title, t.Description, t.SeoTitle, t.SeoDescription }).FirstOrDefault(),
                Category = p.Category.Translations.Where(t => t.Locale == loc).Select(t => t.Name).FirstOrDefault() ?? p.Category.Name,
                CategorySlug = p.Category.Slug,
                CategoryLocale = p.Category.Translations.Any(t => t.Locale == loc) ? loc : CanonicalLocale,
                p.Kind,
                p.Attributes,
                Variants = p.Variants.Where(v => v.IsActive).OrderBy(v => v.Name).Select(v => new { v.Id, v.Sku, v.Name, v.Price, v.ListPrice, v.Currency, Units = sellable.Where(s => s.VariantId == v.Id).Select(s => s.Units).FirstOrDefault() }).ToList(),
                Assets = p.Assets.OrderBy(a => a.SortOrder).Select(a => new AssetDto(a.Id, a.Type.ToString(), a.Url, a.MimeType, a.Width, a.Height, a.SizeBytes, a.Integrity, a.SortOrder)).ToList(),
                Rating = p.Reviews.Where(r => r.IsApproved).Average(r => (decimal?)r.Rating) ?? 0,
                ReviewCount = p.Reviews.Count(r => r.IsApproved)
            }).SingleOrDefaultAsync(token);
        if (product is null) return null;
        var t = product.Translation;
        return new ProductDetailDto(product.Id, product.Slug, t?.Title ?? product.Title, product.Brand, t?.Description ?? product.Description, product.Category,
            product.Variants.Select(v => new VariantDto(v.Id, v.Sku, v.Name, new MoneyDto(v.Price, v.Currency), v.ListPrice == null ? null : new MoneyDto(v.ListPrice.Value, v.Currency), AvailabilityHint(v.Units))).ToList(),
            product.Assets, decimal.Round(product.Rating, 1), product.ReviewCount, product.CategorySlug, product.Kind, product.Attributes.Select(a => new SpecDto(a.Label, a.Value)).ToList(),
            t is null ? CanonicalLocale : loc, t?.SeoTitle, t?.SeoDescription, product.CategoryLocale);
    }

    private IQueryable<CardRow> ProjectCards(IQueryable<Product> query, string loc)
    {
        var sellable = SellableStock.Query(db);
        return query.Select(p => new CardRow(
        p.Id, p.Variants.Where(v => v.IsActive).OrderBy(v => v.Price).Select(v => v.Id).First(), p.Slug,
        p.Translations.Where(t => t.Locale == loc).Select(t => t.Title).FirstOrDefault() ?? p.Title, p.Brand,
        p.Assets.Where(a => a.Type == AssetType.PrimaryImage).OrderBy(a => a.SortOrder).Select(a => new ImageDto(a.Url, a.MimeType, a.Width, a.Height)).FirstOrDefault(),
        p.Variants.Where(v => v.IsActive).OrderBy(v => v.Price).Select(v => v.Price).First(),
        p.Variants.Where(v => v.IsActive).OrderBy(v => v.Price).Select(v => v.ListPrice).First(),
        p.Variants.Where(v => v.IsActive).OrderBy(v => v.Price).Select(v => v.Currency).First(),
        p.Reviews.Where(r => r.IsApproved).Average(r => (decimal?)r.Rating) ?? 0,
        p.Reviews.Count(r => r.IsApproved), p.Variants.Any(v => v.IsActive && sellable.Any(s => s.VariantId == v.Id && s.Units > 0)), p.IsFeatured, p.Kind, p.Attributes,
        p.Translations.Any(t => t.Locale == loc) ? loc : CanonicalLocale));
    }

    private static ProductCardDto MapCard(CardRow x) => new(x.Id, x.DefaultVariantId, x.Slug, x.Title, x.Brand, x.Image, new MoneyDto(x.Price, x.Currency), x.ListPrice is null ? null : new MoneyDto(x.ListPrice.Value, x.Currency), decimal.Round(x.Rating, 1), x.ReviewCount, x.Available ? "in_stock" : "out_of_stock", x.Featured ? ["featured"] : [], x.Kind, Highlights(x.Attributes), x.Locale);

    public static string AvailabilityHint(int sellableUnits) => sellableUnits > 10 ? "in_stock" : sellableUnits > 0 ? "low_stock" : "out_of_stock";

    /// <summary>The attributes a card shows: those flagged as highlights, in catalog order, at most three.</summary>
    public static IReadOnlyList<SpecDto> Highlights(IEnumerable<ProductAttribute> attributes) =>
        attributes.Where(a => a.Highlight && !string.IsNullOrWhiteSpace(a.Value)).Take(3).Select(a => new SpecDto(a.Label, a.Value)).ToList();

    private static void ValidateSearch(SearchRequest request)
    {
        if (request.Page < 1 || request.PageSize is < 1 or > 100) throw CommerceErrors.Validation("Page must be positive and pageSize must be between 1 and 100.");
        if (request.Query?.Length > 120) throw CommerceErrors.Validation("Search query cannot exceed 120 characters.");
        if (request.MinPrice < 0 || request.MaxPrice < 0 || request.MinPrice > request.MaxPrice) throw CommerceErrors.Validation("Price range is invalid.");
        if (request.MinimumRating is < 0 or > 5) throw CommerceErrors.Validation("Minimum rating must be between 0 and 5.");
    }

    private sealed record CardRow(Guid Id, Guid DefaultVariantId, string Slug, string Title, string Brand, ImageDto? Image, decimal Price, decimal? ListPrice, string Currency, decimal Rating, int ReviewCount, bool Available, bool Featured, string Kind, List<ProductAttribute> Attributes, string Locale);
}
