using Commerce.Contracts;

namespace Commerce.Application;

public sealed class StorefrontService(CatalogService catalog, IReadModelCache cache)
{
    public Task<HomeDto> GetHomeAsync(CancellationToken cancellationToken) => cache.GetOrCreateAsync(
        "storefront:home:v1",
        async token =>
        {
            // A scoped EF context cannot execute concurrent queries. These remain two
            // bounded projections, while the aggregate removes extra client round trips.
            var categories = await catalog.GetCategoriesAsync(token);
            var products = await catalog.GetFeaturedAsync(12, token);
            return new HomeDto(categories, new HeroDto("Everyday essentials", "Everything you need, delivered", "Fast browsing backed by authoritative commerce data.", products.FirstOrDefault()?.Slug), [new ProductRailDto("featured", "Featured picks", products)], false);
        }, TimeSpan.FromMinutes(2), ["homepage", "products", "categories"], cancellationToken);

    public async Task<StorefrontProductDto> GetProductAsync(string slug, CancellationToken cancellationToken)
    {
        var product = await catalog.GetProductAsync(slug, cancellationToken);
        try
        {
            var related = await catalog.GetRelatedAsync(product.Id, 8,cancellationToken);
            return new StorefrontProductDto(product, related, false);
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            return new StorefrontProductDto(product, [], true);
        }
    }
}
