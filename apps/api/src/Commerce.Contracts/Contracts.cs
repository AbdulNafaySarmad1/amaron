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
public sealed record PaymentMethodRequest(string Method, string? Provider = null, string? PaymentMethodToken = null, int? InstallmentCount = null);
public sealed record CheckoutRequest(AddressRequest ShippingAddress, PaymentMethodRequest? PaymentMethod = null);
public sealed record OrderItemDto(Guid VariantId, string Sku, string ProductTitle, string VariantName, int Quantity, MoneyDto UnitPrice, MoneyDto LineTotal);
public sealed record OrderDto(Guid Id, string OrderNumber, string Status, MoneyDto Subtotal, DateTimeOffset CreatedAt, IReadOnlyList<OrderItemDto> Items);
public sealed record PaymentAttemptDto(Guid Id, string Provider, string? ProviderReference, MoneyDto Amount, string Method, string Status, string? FailureCategory, bool AuthenticationRequired, DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt);
public sealed record PaymentDto(Guid Id, Guid OrderId, string? CustomerId, MoneyDto Authorized, MoneyDto Captured, MoneyDto Refunded, string Status, string Provider, string? ProviderReference, string Method, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, DateTimeOffset ExpiresAt, IReadOnlyList<PaymentAttemptDto> Attempts);
public sealed record CheckoutResultDto(OrderDto Order, PaymentDto Payment, bool IdempotencyReplayed);
public sealed record ConfirmPaymentRequest(string? PaymentMethodToken = null);
public sealed record CapturePaymentRequest(decimal? Amount, string Reason);
public sealed record RefundRequest(decimal Amount, string Reason);
public sealed record RefundDto(Guid Id, Guid PaymentId, Guid OrderId, MoneyDto Amount, string Reason, string Status, string? ProviderRefundReference, string RequestedBy, DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt);
public sealed record PaymentProviderCapabilityDto(string Provider, bool SupportsAuthorization, bool SupportsManualCapture, bool SupportsPartialCapture, bool SupportsPartialRefund, bool SupportsInstallments, bool SupportsSavedMethods, bool Supports3Ds, bool SupportsAsyncPayments, IReadOnlyList<string> Methods);
public sealed record InstallmentQuoteRequest(Guid OrderId, int InstallmentCount, string Provider = "test");
public sealed record InstallmentPlanDto(Guid Id, Guid PaymentId, string Provider, string? ProviderPlanId, MoneyDto Total, int InstallmentCount, string Frequency, MoneyDto CustomerCost, MoneyDto? MerchantCost, string Status, string TermsReference, IReadOnlyList<InstallmentScheduleDto> Schedule);
public sealed record InstallmentScheduleDto(int Sequence, DateTimeOffset DueDate, MoneyDto Amount, string Status, DateTimeOffset? PaidAt);
public sealed record ReconciliationDto(Guid Id, Guid PaymentId, string Provider, string Status, MoneyDto ProviderCaptured, MoneyDto ProviderRefunded, string? Detail, DateTimeOffset EvaluatedAt);

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

public sealed record CreateSupplierRequest(string Code, string LegalName, string CountryCode, string? TaxIdentifier);
public sealed record SupplierDto(Guid Id, string Code, string LegalName, string CountryCode, string? TaxIdentifier, string Status, DateTimeOffset CreatedAt);
public sealed record AssignSupplierUserRequest(Guid ApplicationUserId);
public sealed record SupplierSourceRequest(Guid SupplierOrganizationId, Guid VariantId, string SupplierSku, string Currency, decimal UnitCost, int LeadTimeDays, int MinimumOrderQuantity, int OrderMultiple, decimal? ReliabilityPercent);
public sealed record SupplierSourceDto(Guid Id, Guid SupplierOrganizationId, Guid VariantId, string SupplierSku, MoneyDto UnitCost, int LeadTimeDays, int MinimumOrderQuantity, int OrderMultiple, decimal? ReliabilityPercent, bool IsActive);
public sealed record PurchaseOrderLineRequest(Guid SupplierSourceId, int Quantity);
public sealed record CreatePurchaseOrderRequest(Guid SupplierOrganizationId, Guid WarehouseId, DateTimeOffset ExpectedAt, IReadOnlyList<PurchaseOrderLineRequest> Lines);
public sealed record PurchaseOrderLineDto(Guid Id, Guid VariantId, Guid? SourceQuotationLineId, string SupplierSku, int OrderedQuantity, int ReceivedQuantity, MoneyDto UnitCost, int LeadTimeDays, int MinimumOrderQuantity, int OrderMultiple, decimal? ReliabilityPercent);
public sealed record PurchaseOrderDto(Guid Id, string Number, Guid SupplierOrganizationId, Guid WarehouseId, Guid? SourceQuotationId, int Revision, string Status, string Currency, DateTimeOffset ExpectedAt, DateTimeOffset CreatedAt, string CreatedBy, string? ApprovedBy, IReadOnlyList<PurchaseOrderLineDto> Lines);
public sealed record CreateInboundShipmentLineRequest(Guid PurchaseOrderLineId, int ExpectedQuantity);
public sealed record CreateInboundShipmentRequest(Guid PurchaseOrderId, string? Carrier, string? TrackingNumber, DateTimeOffset? EstimatedArrival, IReadOnlyList<CreateInboundShipmentLineRequest> Lines);
public sealed record InboundShipmentDto(Guid Id, string Number, Guid SupplierOrganizationId, Guid PurchaseOrderId, string Status, string? Carrier, string? TrackingNumber, DateTimeOffset? EstimatedArrival, DateTimeOffset CreatedAt, bool IdempotencyReplayed = false);
public sealed record InvoiceLineRequest(Guid? PurchaseOrderLineId, string SupplierSku, int Quantity, decimal UnitCost);
public sealed record InvoiceIngestionRequest(Guid SupplierOrganizationId, Guid? PurchaseOrderId, string Provider, string ExternalNumber, string Currency, decimal TotalAmount, DateTimeOffset IssuedAt, string? DocumentReference, string RawPayload, IReadOnlyList<InvoiceLineRequest> Lines);
public sealed record FiscalInvoiceDto(Guid Id, Guid SupplierOrganizationId, Guid? PurchaseOrderId, string Provider, string ExternalNumber, MoneyDto Total, string Status, DateTimeOffset IssuedAt, DateTimeOffset IngestedAt);
public sealed record ReceiptLineRequest(Guid PurchaseOrderLineId, int ExpectedQuantity, int PhysicalQuantity, int AcceptedQuantity, int DamagedQuantity, int QuarantinedQuantity, string? LotNumber, DateTimeOffset? ExpiresAt);
public sealed record PostGoodsReceiptRequest(Guid PurchaseOrderId, Guid? InboundShipmentId, Guid WarehouseId, IReadOnlyList<ReceiptLineRequest> Lines);
public sealed record GoodsReceiptLineDto(Guid PurchaseOrderLineId, Guid VariantId, int ExpectedQuantity, int PhysicalQuantity, int AcceptedQuantity, int DamagedQuantity, int QuarantinedQuantity, int MissingQuantity, Guid? LotId);
public sealed record GoodsReceiptDto(Guid Id, string Number, Guid PurchaseOrderId, Guid? InboundShipmentId, Guid WarehouseId, string Status, string ReceivedBy, DateTimeOffset ReceivedAt, IReadOnlyList<GoodsReceiptLineDto> Lines, bool IdempotencyReplayed);
public sealed record InventoryBalanceDto(Guid WarehouseId, Guid? LocationId, Guid VariantId, Guid? LotId, string State, int Quantity, DateTimeOffset UpdatedAt);
public sealed record InvoiceMatchDto(Guid Id, Guid PurchaseOrderId, Guid FiscalInvoiceId, string Status, IReadOnlyList<string> Exceptions, DateTimeOffset EvaluatedAt);
public sealed record CreateRfqLineRequest(Guid VariantId, int Quantity);
public sealed record CreateRfqRequest(Guid WarehouseId, string Currency, DateTimeOffset ResponseDeadline, IReadOnlyList<Guid> SupplierOrganizationIds, IReadOnlyList<CreateRfqLineRequest> Lines);
public sealed record RfqLineDto(Guid Id, Guid VariantId, int Quantity);
public sealed record RfqDto(Guid Id, string Number, Guid WarehouseId, string Status, string Currency, DateTimeOffset ResponseDeadline, string CreatedBy, DateTimeOffset CreatedAt, Guid? AwardedQuotationId, IReadOnlyList<Guid> SupplierOrganizationIds, IReadOnlyList<RfqLineDto> Lines);
public sealed record SubmitQuotationLineRequest(Guid RfqLineId, string SupplierSku, decimal UnitCost, int LeadTimeDays, int MinimumOrderQuantity, int OrderMultiple, decimal? ReliabilityPercent = null);
public sealed record SubmitQuotationRequest(string Currency, IReadOnlyList<SubmitQuotationLineRequest> Lines);
public sealed record QuotationLineDto(Guid RfqLineId, Guid VariantId, string SupplierSku, int RequestedQuantity, MoneyDto UnitCost, int LeadTimeDays, int MinimumOrderQuantity, int OrderMultiple, decimal? ReliabilityPercent, decimal ExtendedCost);
public sealed record SupplierQuotationDto(Guid Id, string Number, Guid RfqId, Guid SupplierOrganizationId, string Status, MoneyDto Total, DateTimeOffset SubmittedAt, IReadOnlyList<QuotationLineDto> Lines, bool IdempotencyReplayed = false);
public sealed record AwardQuotationRequest(Guid QuotationId, string Reason);
public sealed record AwardQuotationResultDto(RfqDto Rfq, SupplierQuotationDto Quotation, PurchaseOrderDto DraftPurchaseOrder);
public sealed record CycleCountScopeRequest(Guid VariantId, Guid? LocationId, Guid? LotId, string State);
public sealed record CreateCycleCountRequest(Guid WarehouseId, IReadOnlyList<CycleCountScopeRequest> Lines);
public sealed record SubmitCycleCountLineRequest(Guid LineId, int CountedQuantity);
public sealed record SubmitCycleCountRequest(IReadOnlyList<SubmitCycleCountLineRequest> Lines);
public sealed record CycleCountLineDto(Guid Id, Guid VariantId, Guid? LocationId, Guid? LotId, string State, int? ExpectedQuantity, int? CountedQuantity, int? Difference);
public sealed record CycleCountDto(Guid Id, string Number, Guid WarehouseId, string Status, string CreatedBy, DateTimeOffset CreatedAt, string? CountedBy, string? ReconciledBy, IReadOnlyList<CycleCountLineDto> Lines);
public sealed record WarehouseTaskDto(Guid Id, string Number, Guid WarehouseId, string Type, string Status, int Priority, Guid VariantId, Guid? LotId, Guid? SourceLocationId, Guid? DestinationLocationId, string InventoryState, int Quantity, int CompletedQuantity, string ReferenceType, string ReferenceId, string? AssignedTo, DateTimeOffset CreatedAt);
public sealed record CreateStockTransferLineRequest(Guid VariantId, Guid? LotId, int Quantity);
public sealed record CreateStockTransferRequest(Guid FromWarehouseId, Guid ToWarehouseId, IReadOnlyList<CreateStockTransferLineRequest> Lines);
public sealed record StockTransferLineDto(Guid Id, Guid VariantId, Guid? LotId, int Quantity);
public sealed record StockTransferDto(Guid Id, string Number, Guid FromWarehouseId, Guid ToWarehouseId, string Status, string CreatedBy, DateTimeOffset CreatedAt, string? DispatchedBy, DateTimeOffset? DispatchedAt, string? ReceivedBy, DateTimeOffset? ReceivedAt, IReadOnlyList<StockTransferLineDto> Lines, bool IdempotencyReplayed = false);
