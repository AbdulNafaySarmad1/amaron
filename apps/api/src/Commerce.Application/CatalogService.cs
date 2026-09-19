using Commerce.Contracts;
using Commerce.Domain;
using Microsoft.EntityFrameworkCore;

namespace Commerce.Application;

public sealed class CatalogService(ICommerceDbContext db, IReadModelCache cache)
{
    public async Task<IReadOnlyList<CategoryDto>> GetCategoriesAsync(CancellationToken cancellationToken) =>
        await cache.GetOrCreateAsync(
            "categories:v1",
            async token => await db.Categories.AsNoTracking().OrderBy(x => x.SortOrder).ThenBy(x => x.Name)
                .Select(x => new CategoryDto(x.Id, x.Slug, x.Name, x.ParentId)).ToListAsync(token),
            TimeSpan.FromMinutes(10), ["categories", "homepage"], cancellationToken);

    public async Task<ProductDetailDto> GetProductAsync(string slug, CancellationToken cancellationToken) =>
        await cache.GetOrCreateAsync(
            $"product:v1:{slug.ToLowerInvariant()}",
            async token => await LoadProductAsync(slug, token) ?? throw CommerceErrors.NotFound("Product"),
            TimeSpan.FromMinutes(5), ["products", $"product:{slug.ToLowerInvariant()}"], cancellationToken);

    public async Task<ProductPageDto> SearchAsync(SearchRequest request, CancellationToken cancellationToken)
    {
        ValidateSearch(request);
        var query = db.Products.AsNoTracking().Where(p => p.Status == ProductStatus.Active);
        if (!string.IsNullOrWhiteSpace(request.Query))
        {
            var term = request.Query.Trim().ToLower();
            query = query.Where(p => p.Title.ToLower().Contains(term) || p.Brand.ToLower().Contains(term) || p.Description.ToLower().Contains(term));
        }
        if (!string.IsNullOrWhiteSpace(request.Category)) query = query.Where(p => p.Category.Slug == request.Category);
        if (!string.IsNullOrWhiteSpace(request.Brand)) query = query.Where(p => p.Brand == request.Brand);
        if (request.MinPrice is not null) query = query.Where(p => p.Variants.Any(v => v.IsActive && v.Price >= request.MinPrice));
        if (request.MaxPrice is not null) query = query.Where(p => p.Variants.Any(v => v.IsActive && v.Price <= request.MaxPrice));
        if (request.MinimumRating is not null) query = query.Where(p => p.Reviews.Where(r => r.IsApproved).Average(r => (decimal?)r.Rating) >= request.MinimumRating);
        if (request.Available == true) query = query.Where(p => p.Variants.Any(v => v.IsActive && v.Inventory.QuantityOnHand > 0));

        query = request.Sort?.ToLowerInvariant() switch
        {
            "price-asc" => query.OrderBy(p => p.Variants.Where(v => v.IsActive).Min(v => v.Price)).ThenBy(p => p.Id),
            "price-desc" => query.OrderByDescending(p => p.Variants.Where(v => v.IsActive).Min(v => v.Price)).ThenBy(p => p.Id),
            "rating" => query.OrderByDescending(p => p.Reviews.Where(r => r.IsApproved).Average(r => (decimal?)r.Rating) ?? 0).ThenBy(p => p.Id),
            "newest" => query.OrderByDescending(p => p.CreatedAt).ThenBy(p => p.Id),
            _ => query.OrderBy(p => p.Title).ThenBy(p => p.Id)
        };

        var total = await query.CountAsync(cancellationToken);
        var rows = await ProjectCards(query.Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)).ToListAsync(cancellationToken);
        return new ProductPageDto(rows.Select(MapCard).ToList(), request.Page, request.PageSize, total, (int)Math.Ceiling((double)total / request.PageSize));
    }

    public async Task<IReadOnlyList<SuggestionDto>> SuggestAsync(string query, CancellationToken cancellationToken)
    {
        query = query.Trim();
        if (query.Length < 2) return [];
        if (query.Length > 80) throw CommerceErrors.Validation("The suggestion query cannot exceed 80 characters.");
        return await cache.GetOrCreateAsync(
            $"suggest:v1:{query.ToLowerInvariant()}",
            async token =>
            {
                var normalized = query.ToLower();
                var products = await db.Products.AsNoTracking().Where(p => p.Status == ProductStatus.Active && p.Title.ToLower().StartsWith(normalized))
                    .OrderByDescending(p => p.IsFeatured).ThenBy(p => p.Title).Take(8)
                    .Select(p => new SuggestionDto("product", p.Title, p.Slug)).ToListAsync(token);
                var categories = await db.Categories.AsNoTracking().Where(c => c.Name.ToLower().StartsWith(normalized))
                    .OrderBy(c => c.Name).Take(4).Select(c => new SuggestionDto("category", c.Name, c.Slug)).ToListAsync(token);
                return products.Concat(categories).Take(10).ToList();
            }, TimeSpan.FromMinutes(1), ["search-suggestions"], cancellationToken);
    }

    public async Task<IReadOnlyList<ProductCardDto>> GetBatchAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken)
    {
        if (ids.Count is < 1 or > 50 || ids.Distinct().Count() != ids.Count) throw CommerceErrors.Validation("Provide between 1 and 50 unique product IDs.");
        var rows = await ProjectCards(db.Products.AsNoTracking().Where(p => ids.Contains(p.Id) && p.Status == ProductStatus.Active)).ToListAsync(cancellationToken);
        var byId = rows.Select(MapCard).ToDictionary(x => x.Id);
        return ids.Where(byId.ContainsKey).Select(id => byId[id]).ToList();
    }

    public async Task<IReadOnlyList<ProductCardDto>> GetFeaturedAsync(int count, CancellationToken cancellationToken)
    {
        var rows = await ProjectCards(db.Products.AsNoTracking().Where(p => p.Status == ProductStatus.Active && p.IsFeatured).OrderBy(p => p.Title).Take(Math.Clamp(count, 1, 24))).ToListAsync(cancellationToken);
        return rows.Select(MapCard).ToList();
    }

    public async Task<IReadOnlyList<ProductCardDto>> GetRelatedAsync(Guid productId, string category, int count, CancellationToken cancellationToken)
    {
        var rows = await ProjectCards(db.Products.AsNoTracking().Where(p => p.Status == ProductStatus.Active && p.Id != productId && p.Category.Name == category).OrderByDescending(p => p.IsFeatured).Take(Math.Clamp(count, 1, 12))).ToListAsync(cancellationToken);
        return rows.Select(MapCard).ToList();
    }

    private async Task<ProductDetailDto?> LoadProductAsync(string slug, CancellationToken token)
    {
        var product = await db.Products.AsNoTracking().Where(p => p.Slug == slug && p.Status == ProductStatus.Active)
            .Select(p => new
            {
                p.Id,
                p.Slug,
                p.Title,
                p.Brand,
                p.Description,
                Category = p.Category.Name,
                Variants = p.Variants.Where(v => v.IsActive).OrderBy(v => v.Name).Select(v => new VariantDto(v.Id, v.Sku, v.Name, new MoneyDto(v.Price, v.Currency), v.ListPrice == null ? null : new MoneyDto(v.ListPrice.Value, v.Currency), v.Inventory.QuantityOnHand > 10 ? "in_stock" : v.Inventory.QuantityOnHand > 0 ? "low_stock" : "out_of_stock")).ToList(),
                Assets = p.Assets.OrderBy(a => a.SortOrder).Select(a => new AssetDto(a.Id, a.Type.ToString(), a.Url, a.MimeType, a.Width, a.Height, a.SizeBytes, a.Integrity, a.SortOrder)).ToList(),
                Rating = p.Reviews.Where(r => r.IsApproved).Average(r => (decimal?)r.Rating) ?? 0,
                ReviewCount = p.Reviews.Count(r => r.IsApproved)
            }).SingleOrDefaultAsync(token);
        return product is null ? null : new ProductDetailDto(product.Id, product.Slug, product.Title, product.Brand, product.Description, product.Category, product.Variants, product.Assets, decimal.Round(product.Rating, 1), product.ReviewCount);
    }

    private static IQueryable<CardRow> ProjectCards(IQueryable<Product> query) => query.Select(p => new CardRow(
        p.Id, p.Slug, p.Title, p.Brand,
        p.Assets.Where(a => a.Type == AssetType.PrimaryImage).OrderBy(a => a.SortOrder).Select(a => new ImageDto(a.Url, a.MimeType, a.Width, a.Height)).FirstOrDefault(),
        p.Variants.Where(v => v.IsActive).OrderBy(v => v.Price).Select(v => v.Price).First(),
        p.Variants.Where(v => v.IsActive).OrderBy(v => v.Price).Select(v => v.ListPrice).First(),
        p.Variants.Where(v => v.IsActive).OrderBy(v => v.Price).Select(v => v.Currency).First(),
        p.Reviews.Where(r => r.IsApproved).Average(r => (decimal?)r.Rating) ?? 0,
        p.Reviews.Count(r => r.IsApproved), p.Variants.Any(v => v.IsActive && v.Inventory.QuantityOnHand > 0), p.IsFeatured));

    private static ProductCardDto MapCard(CardRow x) => new(x.Id, x.Slug, x.Title, x.Brand, x.Image, new MoneyDto(x.Price, x.Currency), x.ListPrice is null ? null : new MoneyDto(x.ListPrice.Value, x.Currency), decimal.Round(x.Rating, 1), x.ReviewCount, x.Available ? "in_stock" : "out_of_stock", x.Featured ? ["featured"] : []);

    private static void ValidateSearch(SearchRequest request)
    {
        if (request.Page < 1 || request.PageSize is < 1 or > 100) throw CommerceErrors.Validation("Page must be positive and pageSize must be between 1 and 100.");
        if (request.Query?.Length > 120) throw CommerceErrors.Validation("Search query cannot exceed 120 characters.");
        if (request.MinPrice < 0 || request.MaxPrice < 0 || request.MinPrice > request.MaxPrice) throw CommerceErrors.Validation("Price range is invalid.");
        if (request.MinimumRating is < 0 or > 5) throw CommerceErrors.Validation("Minimum rating must be between 0 and 5.");
    }

    private sealed record CardRow(Guid Id, string Slug, string Title, string Brand, ImageDto? Image, decimal Price, decimal? ListPrice, string Currency, decimal Rating, int ReviewCount, bool Available, bool Featured);
}
