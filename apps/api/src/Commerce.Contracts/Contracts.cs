namespace Commerce.Contracts;

public sealed record MoneyDto(decimal Amount, string Currency);
public sealed record ImageDto(string Url, string MimeType, int? Width, int? Height);
public sealed record ProductCardDto(Guid Id, Guid DefaultVariantId, string Slug, string Title, string Brand, ImageDto? PrimaryImage, MoneyDto Price, MoneyDto? ListPrice, decimal Rating, int ReviewCount, string AvailabilityHint, string[] Badges);
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
public sealed record UpdateProductRequest(string Title, string Brand, string Description, bool IsFeatured, string Status);
public sealed record UpdateInventoryRequest(int QuantityOnHand);
public sealed record AdminProductDto(Guid Id, string Slug, string Title, string Brand, string Description, string Status, bool IsFeatured, string Version);
public sealed record InventoryDto(Guid VariantId, int QuantityOnHand, string Version);

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

public sealed record OperationsMetricDto(string Label, decimal? Value, string Unit, string? Note = null);
public sealed record OperationsDashboardDto(
    IReadOnlyList<OperationsMetricDto> Catalog,
    IReadOnlyList<OperationsMetricDto> Pricing,
    IReadOnlyList<OperationsMetricDto> Inventory,
    IReadOnlyList<OperationsMetricDto> Demand,
    IReadOnlyList<OperationalAlertDto> Alerts,
    IReadOnlyList<AuditEntryDto> RecentActivity,
    DateTimeOffset GeneratedAt);
public sealed record AdminVariantRowDto(Guid ProductId, Guid VariantId, string Sku, string Product, string Variant, string Category, string Status, MoneyDto Price, MoneyDto? UnitCost, int OnHand, int AvailableToSell, string InventoryHealth, decimal? DaysOfSupply);
public sealed record AdminVariantPageDto(IReadOnlyList<AdminVariantRowDto> Items, int Page, int PageSize, int TotalCount, int TotalPages);
public sealed record PriceRecordDto(Guid Id, Guid VariantId, MoneyDto Price, MoneyDto? CompareAtPrice, MoneyDto? CostAtTime, DateTimeOffset EffectiveFrom, DateTimeOffset? EffectiveUntil, string Reason, string Kind, string Status, string Source, int Revision, string CreatedBy, DateTimeOffset CreatedAt, string? ApprovedBy);
public sealed record PriceScheduleRequest(Guid VariantId, decimal Price, decimal? CompareAtPrice, DateTimeOffset EffectiveFrom, DateTimeOffset? EffectiveUntil, string Kind, string Reason);
public sealed record PriceApprovalRequest(string Reason);
public sealed record PricePointDto(DateTimeOffset Timestamp, decimal? Price, int? Units, decimal? Revenue, decimal? GrossMarginPercent, int? Inventory, decimal? ForecastUnits, string SeriesType);
public sealed record PricingCurveDto(Guid VariantId, string Sku, string Product, MoneyDto CurrentPrice, MoneyDto? UnitCost, IReadOnlyList<PricePointDto> Points, string Methodology);
public sealed record PriceSimulationRequest(Guid VariantId, decimal CandidatePrice, int HorizonDays = 30);
public sealed record PriceSimulationDto(Guid VariantId, decimal CurrentPrice, decimal CandidatePrice, bool PredictiveOutputAvailable, string? UnavailableReason, decimal? EstimatedUnits, decimal? Revenue, decimal? GrossProfit, decimal? GrossMarginPercent, decimal? DaysOfSupply, DateTimeOffset? StockoutAt, decimal? ConfidenceLower, decimal? ConfidenceUpper, decimal? Elasticity, string Quality, IReadOnlyList<string> Assumptions);
public sealed record PriceRecommendationDto(Guid Id, Guid VariantId, decimal CurrentPrice, decimal RecommendedPrice, decimal? PredictedDemand, decimal? PredictedRevenue, decimal? PredictedGrossProfit, decimal? ConfidenceLower, decimal? ConfidenceUpper, decimal? ConfidenceLevel, IReadOnlyList<string> ReasonCodes, string ModelVersion, DateTimeOffset GeneratedAt, string Status, string? ApprovedBy);
public sealed record PricingPolicyDto(Guid Id, Guid? CategoryId, string Currency, decimal? MinimumGrossMarginPercent, decimal? MinimumPrice, decimal? MaximumPrice, decimal MaximumChangePercent, decimal MaximumMarkdownPercent, decimal ApprovalThresholdPercent, bool EnforceCostFloor, bool IsActive);
public sealed record DemandObservationDto(Guid VariantId, DateTimeOffset PeriodStart, int ProductViews, int SearchImpressions, int AddToCartCount, int Orders, int UnitsOrdered, int UnitsFulfilled, decimal Revenue, decimal EffectivePrice, bool PromotionActive, bool StockAvailable, string Channel, string? Region, decimal? ConversionRate, decimal? SellThroughRate);
public sealed record ForecastRequest(Guid VariantId, int HorizonDays, int InputWindowDays, string Model);
public sealed record ForecastDto(Guid Id, Guid VariantId, DateTimeOffset HorizonStart, int HorizonDays, decimal PredictedUnits, decimal? ConfidenceLower, decimal? ConfidenceUpper, decimal? ConfidenceLevel, string Model, string ModelVersion, int InputWindowDays, DateTimeOffset GeneratedAt, decimal? Mae, decimal? Rmse, decimal? Wape, decimal? Bias);
public sealed record WarehouseStockDto(Guid WarehouseId, string Warehouse, string? Location, Guid VariantId, string Sku, int OnHand, int Reserved, int SafetyStock, int Unavailable, int Inbound, int AvailableToSell, int SupplierLeadTimeDays, string Health, decimal? AverageDailyDemand, decimal? DaysOfSupply, decimal? ReorderPoint, DateTimeOffset? ProjectedStockoutAt);
public sealed record InventoryAdjustmentRequest(Guid VariantId, Guid WarehouseId, int QuantityDelta, string Reason, string ReferenceId, string Note);
public sealed record InventoryTransferRequest(Guid VariantId, Guid FromWarehouseId, Guid ToWarehouseId, int Quantity, string ReferenceId, string Note);
public sealed record InventoryLedgerEntryDto(Guid Id, Guid VariantId, Guid WarehouseId, Guid? LocationId, int QuantityDelta, string Reason, string ReferenceType, string ReferenceId, string CreatedBy, DateTimeOffset CreatedAt);
public sealed record ReplenishmentDto(Guid Id, Guid VariantId, Guid WarehouseId, int RecommendedQuantity, decimal? AverageDailyDemand, decimal? ReorderPoint, DateTimeOffset? ProjectedStockoutAt, IReadOnlyList<string> Explanation, string ModelVersion, string Status, DateTimeOffset GeneratedAt, string? ApprovedBy);
public sealed record PromotionRequest(string Name, Guid? CategoryId, DateTimeOffset StartsAt, DateTimeOffset EndsAt, decimal DiscountPercent);
public sealed record PromotionDto(Guid Id, string Name, Guid? CategoryId, DateTimeOffset StartsAt, DateTimeOffset EndsAt, decimal DiscountPercent, string Status, string CreatedBy, string? ApprovedBy, DateTimeOffset CreatedAt);
public sealed record OperationalAlertDto(Guid Id, string Type, string Severity, string Title, string Detail, string Status, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record AuditEntryDto(Guid Id, string EventType, string ResourceType, string ResourceId, string ActorId, string Reason, DateTimeOffset CreatedAt, string? BeforeJson, string? AfterJson);
public sealed record BulkPriceItemRequest(Guid VariantId, decimal Price, DateTimeOffset EffectiveFrom, DateTimeOffset? EffectiveUntil, string Reason);
public sealed record BulkPriceRequest(IReadOnlyList<BulkPriceItemRequest> Items);
public sealed record BulkOperationItemDto(Guid VariantId, bool Valid, IReadOnlyList<string> Errors, bool RequiresApproval);
public sealed record BulkOperationPreviewDto(IReadOnlyList<BulkOperationItemDto> Items, int ValidCount, int InvalidCount, bool Applied);
public sealed record ApprovalQueueItemDto(Guid Id, string Type, string ResourceId, string Summary, string Status, DateTimeOffset CreatedAt, string? CreatedBy);
