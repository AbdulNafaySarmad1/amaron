namespace Commerce.Contracts;

public sealed record MoneyDto(decimal Amount, string Currency);
public sealed record ImageDto(string Url, string MimeType, int? Width, int? Height);
public sealed record ProductCardDto(Guid Id, string Slug, string Title, string Brand, ImageDto? PrimaryImage, MoneyDto Price, MoneyDto? ListPrice, decimal Rating, int ReviewCount, string AvailabilityHint, string[] Badges);
public sealed record VariantDto(Guid Id, string Sku, string Name, MoneyDto Price, MoneyDto? ListPrice, string AvailabilityHint);
public sealed record AssetDto(Guid Id, string Type, string Url, string MimeType, int? Width, int? Height, long? SizeBytes, string? Integrity, int SortOrder);
public sealed record ProductDetailDto(Guid Id, string Slug, string Title, string Brand, string Description, string Category, IReadOnlyList<VariantDto> Variants, IReadOnlyList<AssetDto> Assets, decimal Rating, int ReviewCount);
public sealed record CategoryDto(Guid Id, string Slug, string Name, Guid? ParentId);
public sealed record ProductPageDto(IReadOnlyList<ProductCardDto> Items, int Page, int PageSize, int TotalCount, int TotalPages);
public sealed record SearchRequest(string? Query, string? Category, string? Brand, decimal? MinPrice, decimal? MaxPrice, decimal? MinimumRating, bool? Available, string? Sort, int Page = 1, int PageSize = 24);
public sealed record SuggestionDto(string Type, string Value, string? Slug);
public sealed record BatchProductsRequest(IReadOnlyList<Guid> ProductIds);
public sealed record ProductRailDto(string Id, string Title, IReadOnlyList<ProductCardDto> Products);
public sealed record HeroDto(string Eyebrow, string Title, string Subtitle, string? ProductSlug);
public sealed record HomeDto(IReadOnlyList<CategoryDto> Navigation, HeroDto? Hero, IReadOnlyList<ProductRailDto> Rails, bool IsDegraded);
public sealed record StorefrontProductDto(ProductDetailDto Product, IReadOnlyList<ProductCardDto> Recommendations, bool IsDegraded);

public sealed record CartItemDto(Guid VariantId, Guid ProductId, string Slug, string Title, string Variant, ImageDto? Image, int Quantity, MoneyDto UnitPrice, MoneyDto LineTotal, string AvailabilityHint);
public sealed record CartDto(Guid CartId, int TotalQuantity, MoneyDto Subtotal, IReadOnlyList<CartItemDto> Items, string Version);
public sealed record CartSummaryDto(Guid? CartId, int TotalQuantity, MoneyDto Subtotal, string? Version);
public sealed record SetCartItemRequest(Guid VariantId, int Quantity);
public sealed record CartMutationDto(Guid CartId, int TotalQuantity, MoneyDto Subtotal, CartItemDto? ChangedItem, string Version);

public sealed record AddressRequest(string Recipient, string Line1, string? Line2, string City, string Region, string PostalCode, string CountryCode);
public sealed record CheckoutRequest(AddressRequest ShippingAddress);
public sealed record OrderItemDto(Guid VariantId, string Sku, string ProductTitle, string VariantName, int Quantity, MoneyDto UnitPrice, MoneyDto LineTotal);
public sealed record OrderDto(Guid Id, string OrderNumber, string Status, MoneyDto Subtotal, DateTimeOffset CreatedAt, IReadOnlyList<OrderItemDto> Items);
public sealed record CheckoutResultDto(OrderDto Order, bool IdempotencyReplayed);
