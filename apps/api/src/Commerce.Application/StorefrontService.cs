using Commerce.Contracts;

namespace Commerce.Application;

public sealed class StorefrontService(CatalogService catalog, IReadModelCache cache)
{
    public Task<HomeDto> GetHomeAsync(string? locale, CancellationToken cancellationToken) => cache.GetOrCreateAsync(
        $"storefront:home:v2:{CatalogService.ContentLocale(locale)}",
        async token =>
        {
            // A scoped EF context cannot execute concurrent queries. These remain two
            // bounded projections, while the aggregate removes extra client round trips.
            var categories = await catalog.GetCategoriesAsync(locale, token);
            var products = await catalog.GetFeaturedAsync(12, locale, token);
            return new HomeDto(categories, new HeroDto("Everyday essentials", "Everything you need, delivered", "Fast browsing backed by authoritative commerce data.", products.FirstOrDefault()?.Slug), [new ProductRailDto("featured", "Featured picks", products)], false);
        }, TimeSpan.FromMinutes(2), ["homepage", "products", "categories"], cancellationToken);

    public async Task<StorefrontProductDto> GetProductAsync(string slug, string? locale, CancellationToken cancellationToken)
    {
        var product = await catalog.GetProductAsync(slug, locale, cancellationToken);
        try
        {
            var related = await catalog.GetRelatedAsync(product.Id, 8, locale, cancellationToken);
            var relationships = await catalog.GetRelationshipsAsync(product.Id, locale, cancellationToken);
            return new StorefrontProductDto(product, related, false, relationships);
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            return new StorefrontProductDto(product, [], true, []);
        }
    }
}
