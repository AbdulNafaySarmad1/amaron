namespace Commerce.Domain;

public enum ProductStatus { Draft, Active, Archived }
public enum AssetType { PrimaryImage, GalleryImage, Thumbnail, Model3D, Poster }
public enum OrderStatus { Placed, Cancelled }

public sealed class Category
{
    public Guid Id { get; set; }
    public Guid? ParentId { get; set; }
    public string Slug { get; set; } = "";
    public string Name { get; set; } = "";
    public int SortOrder { get; set; }
    public Category? Parent { get; set; }
    public List<Product> Products { get; set; } = [];
}

public sealed class Product
{
    public Guid Id { get; set; }
    public Guid CategoryId { get; set; }
    public string Slug { get; set; } = "";
    public string Title { get; set; } = "";
    public string Brand { get; set; } = "";
    public string Description { get; set; } = "";
    public ProductStatus Status { get; set; } = ProductStatus.Active;
    public bool IsFeatured { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public uint Version { get; set; }
    public Category Category { get; set; } = null!;
    public List<ProductVariant> Variants { get; set; } = [];
    public List<ProductAsset> Assets { get; set; } = [];
    public List<Review> Reviews { get; set; } = [];
}

public sealed class ProductVariant
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public string Sku { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public decimal? ListPrice { get; set; }
    public string Currency { get; set; } = "USD";
    public bool IsActive { get; set; } = true;
    public Product Product { get; set; } = null!;
    public InventoryItem Inventory { get; set; } = null!;
}

public sealed class InventoryItem
{
    public Guid VariantId { get; set; }
    public int QuantityOnHand { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public uint Version { get; set; }
    public ProductVariant Variant { get; set; } = null!;
}

public sealed class ProductAsset
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public AssetType Type { get; set; }
    public string Url { get; set; } = "";
    public string MimeType { get; set; } = "";
    public int? Width { get; set; }
    public int? Height { get; set; }
    public long? SizeBytes { get; set; }
    public string? Integrity { get; set; }
    public int SortOrder { get; set; }
    public Product Product { get; set; } = null!;
}

public sealed class Review
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public int Rating { get; set; }
    public bool IsApproved { get; set; }
    public Product Product { get; set; } = null!;
}

public sealed class Cart
{
    public Guid Id { get; set; }
    public string CustomerId { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; }
    public uint Version { get; set; }
    public List<CartItem> Items { get; set; } = [];
}

public sealed class CartItem
{
    public Guid CartId { get; set; }
    public Guid VariantId { get; set; }
    public int Quantity { get; set; }
    public DateTimeOffset AddedAt { get; set; }
    public Cart Cart { get; set; } = null!;
    public ProductVariant Variant { get; set; } = null!;
}

public sealed class Order
{
    public Guid Id { get; set; }
    public string OrderNumber { get; set; } = "";
    public string CustomerId { get; set; } = "";
    public OrderStatus Status { get; set; }
    public string Currency { get; set; } = "USD";
    public decimal Subtotal { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string Recipient { get; set; } = "";
    public string AddressLine1 { get; set; } = "";
    public string? AddressLine2 { get; set; }
    public string City { get; set; } = "";
    public string Region { get; set; } = "";
    public string PostalCode { get; set; } = "";
    public string CountryCode { get; set; } = "";
    public List<OrderItem> Items { get; set; } = [];
}

public sealed class OrderItem
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public Guid VariantId { get; set; }
    public string Sku { get; set; } = "";
    public string ProductTitle { get; set; } = "";
    public string VariantName { get; set; } = "";
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public decimal LineTotal { get; set; }
    public Order Order { get; set; } = null!;
}

public sealed class IdempotencyRecord
{
    public Guid Id { get; set; }
    public string CustomerId { get; set; } = "";
    public string Key { get; set; } = "";
    public string RequestHash { get; set; } = "";
    public Guid OrderId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
