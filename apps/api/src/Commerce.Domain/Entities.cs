namespace Commerce.Domain;

public enum ProductStatus { Draft, Active, Archived }
public enum AssetType { PrimaryImage, GalleryImage, Thumbnail, Model3D, Poster }
public enum OrderStatus { Placed, Cancelled }
public enum PriceKind { Regular, Promotion, Markdown }
public enum OperationalStatus { Proposed, ReviewRequired, Approved, Rejected, Scheduled, Applied, Expired }
public enum ForecastModel { Naive, MovingAverage, ExponentialSmoothing }
public enum InventoryHealth { Healthy, LowStock, StockoutRisk, OutOfStock, ExcessStock, SlowMoving }
public enum InventoryMovementReason { GoodsReceived, OrderReserved, ReservationReleased, OrderFulfilled, ReturnReceived, TransferOut, TransferIn, Damage, Correction, CycleCount }
public enum AlertStatus { Open, Acknowledged, Resolved }

public sealed class ApplicationUser
{
    public Guid Id { get; set; }
    public string IdentityIssuer { get; set; } = "";
    public string ExternalSubject { get; set; } = "";
    public string? DisplayName { get; set; }
    public string? Email { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

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
    public decimal? UnitCost { get; set; }
    public decimal? VariableCostPerUnit { get; set; }
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

public sealed class PriceRecord
{
    public Guid Id { get; set; }
    public Guid VariantId { get; set; }
    public string Currency { get; set; } = "USD";
    public decimal Price { get; set; }
    public decimal? CompareAtPrice { get; set; }
    public decimal? CostAtTime { get; set; }
    public DateTimeOffset EffectiveFrom { get; set; }
    public DateTimeOffset? EffectiveUntil { get; set; }
    public string Reason { get; set; } = "";
    public Guid? PromotionId { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public string Source { get; set; } = "Manual";
    public int Revision { get; set; }
    public PriceKind Kind { get; set; }
    public OperationalStatus Status { get; set; }
    public ProductVariant Variant { get; set; } = null!;
}

public sealed class PricingPolicy
{
    public Guid Id { get; set; }
    public Guid? CategoryId { get; set; }
    public string Currency { get; set; } = "USD";
    public decimal? MinimumGrossMarginPercent { get; set; }
    public decimal? MinimumPrice { get; set; }
    public decimal? MaximumPrice { get; set; }
    public decimal MaximumChangePercent { get; set; } = 10;
    public decimal MaximumMarkdownPercent { get; set; } = 40;
    public decimal ApprovalThresholdPercent { get; set; } = 10;
    public bool EnforceCostFloor { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public string UpdatedBy { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class PriceRecommendation
{
    public Guid Id { get; set; }
    public Guid VariantId { get; set; }
    public decimal CurrentPrice { get; set; }
    public decimal RecommendedPrice { get; set; }
    public decimal? PredictedDemand { get; set; }
    public decimal? PredictedRevenue { get; set; }
    public decimal? PredictedGrossProfit { get; set; }
    public decimal? ConfidenceLower { get; set; }
    public decimal? ConfidenceUpper { get; set; }
    public decimal? ConfidenceLevel { get; set; }
    public string ReasonCodesJson { get; set; } = "[]";
    public string ModelVersion { get; set; } = "";
    public DateTimeOffset GeneratedAt { get; set; }
    public OperationalStatus Status { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTimeOffset? EffectiveAt { get; set; }
}

public sealed class Promotion
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public Guid? CategoryId { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
    public decimal DiscountPercent { get; set; }
    public OperationalStatus Status { get; set; }
    public string CreatedBy { get; set; } = "";
    public string? ApprovedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class DemandObservation
{
    public Guid Id { get; set; }
    public Guid VariantId { get; set; }
    public DateTimeOffset PeriodStart { get; set; }
    public TimeSpan PeriodDuration { get; set; }
    public int ProductViews { get; set; }
    public int SearchImpressions { get; set; }
    public int AddToCartCount { get; set; }
    public int Orders { get; set; }
    public int UnitsOrdered { get; set; }
    public int UnitsFulfilled { get; set; }
    public decimal Revenue { get; set; }
    public decimal EffectivePrice { get; set; }
    public bool PromotionActive { get; set; }
    public bool StockAvailable { get; set; }
    public string Channel { get; set; } = "storefront";
    public string? Region { get; set; }
}

public sealed class DemandForecast
{
    public Guid Id { get; set; }
    public Guid VariantId { get; set; }
    public DateTimeOffset HorizonStart { get; set; }
    public int HorizonDays { get; set; }
    public decimal PredictedUnits { get; set; }
    public decimal? ConfidenceLower { get; set; }
    public decimal? ConfidenceUpper { get; set; }
    public decimal? ConfidenceLevel { get; set; }
    public ForecastModel Model { get; set; }
    public string ModelVersion { get; set; } = "";
    public int InputWindowDays { get; set; }
    public DateTimeOffset GeneratedAt { get; set; }
    public decimal? Mae { get; set; }
    public decimal? Rmse { get; set; }
    public decimal? Wape { get; set; }
    public decimal? Bias { get; set; }
}

public sealed class Warehouse
{
    public Guid Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;
}

public sealed class InventoryLocation
{
    public Guid Id { get; set; }
    public Guid WarehouseId { get; set; }
    public string Zone { get; set; } = "";
    public string Bin { get; set; } = "";
}

public sealed class WarehouseStock
{
    public Guid WarehouseId { get; set; }
    public Guid VariantId { get; set; }
    public Guid? LocationId { get; set; }
    public int OnHand { get; set; }
    public int Reserved { get; set; }
    public int SafetyStock { get; set; }
    public int Unavailable { get; set; }
    public int Inbound { get; set; }
    public int SupplierLeadTimeDays { get; set; } = 7;
    public DateTimeOffset UpdatedAt { get; set; }
    public uint Version { get; set; }
}

public sealed class InventoryLedgerEntry
{
    public Guid Id { get; set; }
    public Guid VariantId { get; set; }
    public Guid WarehouseId { get; set; }
    public Guid? LocationId { get; set; }
    public int QuantityDelta { get; set; }
    public InventoryMovementReason Reason { get; set; }
    public string ReferenceType { get; set; } = "";
    public string ReferenceId { get; set; } = "";
    public string CreatedBy { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class ReplenishmentRecommendation
{
    public Guid Id { get; set; }
    public Guid VariantId { get; set; }
    public Guid WarehouseId { get; set; }
    public int RecommendedQuantity { get; set; }
    public decimal? AverageDailyDemand { get; set; }
    public decimal? ReorderPoint { get; set; }
    public DateTimeOffset? ProjectedStockoutAt { get; set; }
    public string ExplanationJson { get; set; } = "[]";
    public string ModelVersion { get; set; } = "";
    public OperationalStatus Status { get; set; }
    public DateTimeOffset GeneratedAt { get; set; }
    public string? ApprovedBy { get; set; }
}

public sealed class OperationalAlert
{
    public Guid Id { get; set; }
    public string Type { get; set; } = "";
    public string DeduplicationKey { get; set; } = "";
    public string Severity { get; set; } = "Warning";
    public string Title { get; set; } = "";
    public string Detail { get; set; } = "";
    public AlertStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string? AcknowledgedBy { get; set; }
}

public sealed class OperationsAuditEntry
{
    public Guid Id { get; set; }
    public string EventType { get; set; } = "";
    public string ResourceType { get; set; } = "";
    public string ResourceId { get; set; } = "";
    public string ActorId { get; set; } = "";
    public string? BeforeJson { get; set; }
    public string? AfterJson { get; set; }
    public string Reason { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}
