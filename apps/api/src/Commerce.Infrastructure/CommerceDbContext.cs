using Commerce.Application;
using Commerce.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Commerce.Infrastructure;

public sealed class CommerceDbContext(DbContextOptions<CommerceDbContext> options) : DbContext(options), ICommerceDbContext
{
    public DbSet<ApplicationUser> ApplicationUsers => Set<ApplicationUser>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();
    public DbSet<InventoryItem> Inventory => Set<InventoryItem>();
    public DbSet<ProductAsset> ProductAssets => Set<ProductAsset>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<Cart> Carts => Set<Cart>();
    public DbSet<CartItem> CartItems => Set<CartItem>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();
    public DbSet<PriceRecord> PriceRecords => Set<PriceRecord>();
    public DbSet<PricingPolicy> PricingPolicies => Set<PricingPolicy>();
    public DbSet<PriceRecommendation> PriceRecommendations => Set<PriceRecommendation>();
    public DbSet<Promotion> Promotions => Set<Promotion>();
    public DbSet<DemandObservation> DemandObservations => Set<DemandObservation>();
    public DbSet<DemandForecast> DemandForecasts => Set<DemandForecast>();
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<InventoryLocation> InventoryLocations => Set<InventoryLocation>();
    public DbSet<WarehouseStock> WarehouseStocks => Set<WarehouseStock>();
    public DbSet<InventoryLedgerEntry> InventoryLedgerEntries => Set<InventoryLedgerEntry>();
    public DbSet<ReplenishmentRecommendation> ReplenishmentRecommendations => Set<ReplenishmentRecommendation>();
    public DbSet<OperationalAlert> OperationalAlerts => Set<OperationalAlert>();
    public DbSet<OperationsAuditEntry> OperationsAuditEntries => Set<OperationsAuditEntry>();
    public DbSet<SupplierOrganization> SupplierOrganizations => Set<SupplierOrganization>();
    public DbSet<SupplierUser> SupplierUsers => Set<SupplierUser>();
    public DbSet<SupplierProductSource> SupplierProductSources => Set<SupplierProductSource>();
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<PurchaseOrderLine> PurchaseOrderLines => Set<PurchaseOrderLine>();
    public DbSet<InboundShipment> InboundShipments => Set<InboundShipment>();
    public DbSet<InboundShipmentLine> InboundShipmentLines => Set<InboundShipmentLine>();
    public DbSet<FiscalInvoice> FiscalInvoices => Set<FiscalInvoice>();
    public DbSet<FiscalInvoiceLine> FiscalInvoiceLines => Set<FiscalInvoiceLine>();
    public DbSet<InventoryLot> InventoryLots => Set<InventoryLot>();
    public DbSet<InventoryBalance> InventoryBalances => Set<InventoryBalance>();
    public DbSet<GoodsReceipt> GoodsReceipts => Set<GoodsReceipt>();
    public DbSet<GoodsReceiptLine> GoodsReceiptLines => Set<GoodsReceiptLine>();
    public DbSet<InvoiceMatch> InvoiceMatches => Set<InvoiceMatch>();
    public DbSet<RequestForQuotation> RequestsForQuotation => Set<RequestForQuotation>();
    public DbSet<RequestForQuotationLine> RequestForQuotationLines => Set<RequestForQuotationLine>();
    public DbSet<RequestForQuotationSupplier> RequestForQuotationSuppliers => Set<RequestForQuotationSupplier>();
    public DbSet<SupplierQuotation> SupplierQuotations => Set<SupplierQuotation>();
    public DbSet<SupplierQuotationLine> SupplierQuotationLines => Set<SupplierQuotationLine>();
    public DbSet<WarehouseTask> WarehouseTasks => Set<WarehouseTask>();
    public DbSet<CycleCount> CycleCounts => Set<CycleCount>();
    public DbSet<CycleCountLine> CycleCountLines => Set<CycleCountLine>();
    public DbSet<StockTransfer> StockTransfers => Set<StockTransfer>();
    public DbSet<StockTransferLine> StockTransferLines => Set<StockTransferLine>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.HasPostgresExtension("pg_trgm");
        model.Entity<ApplicationUser>(e =>
        {
            e.ToTable("application_users"); e.HasKey(x => x.Id); e.Property(x => x.IdentityIssuer).HasMaxLength(500); e.Property(x => x.ExternalSubject).HasMaxLength(200); e.Property(x => x.DisplayName).HasMaxLength(200); e.Property(x => x.Email).HasMaxLength(320);
            e.HasIndex(x => new { x.IdentityIssuer, x.ExternalSubject }).IsUnique();
        });
        model.Entity<Category>(e =>
        {
            e.ToTable("categories"); e.HasKey(x => x.Id); e.Property(x => x.Slug).HasMaxLength(160); e.Property(x => x.Name).HasMaxLength(160);
            e.HasIndex(x => x.Slug).IsUnique(); e.HasIndex(x => new { x.ParentId, x.SortOrder }); e.HasOne(x => x.Parent).WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<Product>(e =>
        {
            e.ToTable("products"); e.HasKey(x => x.Id); e.Property(x => x.Slug).HasMaxLength(160); e.Property(x => x.Title).HasMaxLength(240); e.Property(x => x.Brand).HasMaxLength(120); e.Property(x => x.Description).HasMaxLength(8000); e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20); e.Property(x => x.Version).IsRowVersion();
            e.HasIndex(x => x.Slug).IsUnique(); e.HasIndex(x => new { x.CategoryId, x.Status, x.Id }); e.HasIndex(x => x.Title).HasMethod("gin").HasOperators("gin_trgm_ops"); e.HasOne(x => x.Category).WithMany(x => x.Products).HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<ProductVariant>(e =>
        {
            e.ToTable("product_variants"); e.HasKey(x => x.Id); e.Property(x => x.Sku).HasMaxLength(64); e.Property(x => x.Name).HasMaxLength(160); e.Property(x => x.Price).HasPrecision(19, 4); e.Property(x => x.ListPrice).HasPrecision(19, 4); e.Property(x => x.UnitCost).HasPrecision(19, 4); e.Property(x => x.VariableCostPerUnit).HasPrecision(19, 4); e.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
            e.HasIndex(x => x.Sku).IsUnique(); e.HasIndex(x => new { x.ProductId, x.IsActive }); e.ToTable(t => t.HasCheckConstraint("ck_variants_price", "\"Price\" >= 0 AND (\"ListPrice\" IS NULL OR \"ListPrice\" >= \"Price\")")); e.HasOne(x => x.Product).WithMany(x => x.Variants).HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<InventoryItem>(e =>
        {
            e.ToTable("inventory"); e.HasKey(x => x.VariantId); e.Property(x => x.Version).IsRowVersion(); e.ToTable(t => t.HasCheckConstraint("ck_inventory_nonnegative", "\"QuantityOnHand\" >= 0")); e.HasOne(x => x.Variant).WithOne(x => x.Inventory).HasForeignKey<InventoryItem>(x => x.VariantId).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<ProductAsset>(e =>
        {
            e.ToTable("product_assets"); e.HasKey(x => x.Id); e.Property(x => x.Type).HasConversion<string>().HasMaxLength(30); e.Property(x => x.Url).HasMaxLength(1000); e.Property(x => x.MimeType).HasMaxLength(100); e.Property(x => x.Integrity).HasMaxLength(200); e.HasIndex(x => new { x.ProductId, x.SortOrder }); e.HasOne(x => x.Product).WithMany(x => x.Assets).HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<Review>(e =>
        {
            e.ToTable("reviews"); e.HasKey(x => x.Id); e.HasIndex(x => new { x.ProductId, x.IsApproved }); e.ToTable(t => t.HasCheckConstraint("ck_reviews_rating", "\"Rating\" BETWEEN 1 AND 5")); e.HasOne(x => x.Product).WithMany(x => x.Reviews).HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<Cart>(e =>
        {
            e.ToTable("carts"); e.HasKey(x => x.Id); e.Property(x => x.CustomerId).HasMaxLength(200); e.Property(x => x.Version).IsRowVersion(); e.HasIndex(x => x.CustomerId).IsUnique(); e.HasIndex(x => x.UpdatedAt);
        });
        model.Entity<CartItem>(e =>
        {
            e.ToTable("cart_items"); e.HasKey(x => new { x.CartId, x.VariantId }); e.ToTable(t => t.HasCheckConstraint("ck_cart_items_quantity", "\"Quantity\" BETWEEN 1 AND 99")); e.HasIndex(x => x.VariantId); e.HasOne(x => x.Cart).WithMany(x => x.Items).HasForeignKey(x => x.CartId).OnDelete(DeleteBehavior.Cascade); e.HasOne(x => x.Variant).WithMany().HasForeignKey(x => x.VariantId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<Order>(e =>
        {
            e.ToTable("orders"); e.HasKey(x => x.Id); e.Property(x => x.OrderNumber).HasMaxLength(32); e.Property(x => x.CustomerId).HasMaxLength(200); e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20); e.Property(x => x.Currency).HasMaxLength(3).IsFixedLength(); e.Property(x => x.Subtotal).HasPrecision(19, 4); e.Property(x => x.Recipient).HasMaxLength(120); e.Property(x => x.AddressLine1).HasMaxLength(200); e.Property(x => x.AddressLine2).HasMaxLength(200); e.Property(x => x.City).HasMaxLength(100); e.Property(x => x.Region).HasMaxLength(100); e.Property(x => x.PostalCode).HasMaxLength(24); e.Property(x => x.CountryCode).HasMaxLength(2).IsFixedLength();
            e.HasIndex(x => x.OrderNumber).IsUnique(); e.HasIndex(x => new { x.CustomerId, x.CreatedAt, x.Id });
        });
        model.Entity<OrderItem>(e =>
        {
            e.ToTable("order_items"); e.HasKey(x => x.Id); e.Property(x => x.Sku).HasMaxLength(64); e.Property(x => x.ProductTitle).HasMaxLength(240); e.Property(x => x.VariantName).HasMaxLength(160); e.Property(x => x.UnitPrice).HasPrecision(19, 4); e.Property(x => x.LineTotal).HasPrecision(19, 4); e.ToTable(t => t.HasCheckConstraint("ck_order_items_quantity", "\"Quantity\" > 0")); e.HasIndex(x => x.OrderId); e.HasOne(x => x.Order).WithMany(x => x.Items).HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<IdempotencyRecord>(e =>
        {
            e.ToTable("idempotency_records"); e.HasKey(x => x.Id); e.Property(x => x.CustomerId).HasMaxLength(200); e.Property(x => x.Key).HasMaxLength(128); e.Property(x => x.RequestHash).HasMaxLength(64).IsFixedLength(); e.HasIndex(x => new { x.CustomerId, x.Key }).IsUnique(); e.HasIndex(x => x.CreatedAt);
        });
        model.Entity<PriceRecord>(e =>
        {
            e.ToTable("price_records"); e.HasKey(x => x.Id); e.Property(x => x.Currency).HasMaxLength(3).IsFixedLength(); e.Property(x => x.Price).HasPrecision(19, 4); e.Property(x => x.CompareAtPrice).HasPrecision(19, 4); e.Property(x => x.CostAtTime).HasPrecision(19, 4); e.Property(x => x.Reason).HasMaxLength(500); e.Property(x => x.CreatedBy).HasMaxLength(64); e.Property(x => x.ApprovedBy).HasMaxLength(64); e.Property(x => x.Source).HasMaxLength(40); e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20); e.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
            e.HasIndex(x => new { x.VariantId, x.EffectiveFrom, x.EffectiveUntil }); e.HasIndex(x => new { x.Status, x.EffectiveFrom }); e.HasOne(x => x.Variant).WithMany().HasForeignKey(x => x.VariantId).OnDelete(DeleteBehavior.Restrict); e.HasOne<Promotion>().WithMany().HasForeignKey(x => x.PromotionId).OnDelete(DeleteBehavior.Restrict); e.ToTable(t => t.HasCheckConstraint("ck_price_records_price", "\"Price\" >= 0 AND (\"CompareAtPrice\" IS NULL OR \"CompareAtPrice\" >= \"Price\")"));
        });
        model.Entity<PricingPolicy>(e =>
        {
            e.ToTable("pricing_policies"); e.HasKey(x => x.Id); e.Property(x => x.Currency).HasMaxLength(3).IsFixedLength(); e.Property(x => x.MinimumGrossMarginPercent).HasPrecision(8, 4); e.Property(x => x.MinimumPrice).HasPrecision(19, 4); e.Property(x => x.MaximumPrice).HasPrecision(19, 4); e.Property(x => x.MaximumChangePercent).HasPrecision(8, 4); e.Property(x => x.MaximumMarkdownPercent).HasPrecision(8, 4); e.Property(x => x.ApprovalThresholdPercent).HasPrecision(8, 4); e.Property(x => x.UpdatedBy).HasMaxLength(64); e.HasIndex(x => new { x.CategoryId, x.Currency, x.IsActive }); e.HasOne<Category>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<PriceRecommendation>(e =>
        {
            e.ToTable("price_recommendations"); e.HasKey(x => x.Id); e.Property(x => x.CurrentPrice).HasPrecision(19, 4); e.Property(x => x.RecommendedPrice).HasPrecision(19, 4); e.Property(x => x.PredictedDemand).HasPrecision(19, 4); e.Property(x => x.PredictedRevenue).HasPrecision(19, 4); e.Property(x => x.PredictedGrossProfit).HasPrecision(19, 4); e.Property(x => x.ConfidenceLower).HasPrecision(19, 4); e.Property(x => x.ConfidenceUpper).HasPrecision(19, 4); e.Property(x => x.ConfidenceLevel).HasPrecision(6, 4); e.Property(x => x.ReasonCodesJson).HasColumnType("jsonb"); e.Property(x => x.ModelVersion).HasMaxLength(100); e.Property(x => x.Status).HasConversion<string>().HasMaxLength(30); e.Property(x => x.ApprovedBy).HasMaxLength(64); e.HasIndex(x => new { x.Status, x.GeneratedAt }); e.HasIndex(x => new { x.VariantId, x.GeneratedAt }); e.HasOne<ProductVariant>().WithMany().HasForeignKey(x => x.VariantId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<Promotion>(e =>
        {
            e.ToTable("promotions"); e.HasKey(x => x.Id); e.Property(x => x.Name).HasMaxLength(200); e.Property(x => x.DiscountPercent).HasPrecision(8, 4); e.Property(x => x.Status).HasConversion<string>().HasMaxLength(30); e.Property(x => x.CreatedBy).HasMaxLength(64); e.Property(x => x.ApprovedBy).HasMaxLength(64); e.HasIndex(x => new { x.CategoryId, x.StartsAt, x.EndsAt }); e.HasOne<Category>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict); e.ToTable(t => t.HasCheckConstraint("ck_promotions_discount", "\"DiscountPercent\" > 0 AND \"DiscountPercent\" <= 100 AND \"EndsAt\" > \"StartsAt\""));
        });
        model.Entity<DemandObservation>(e =>
        {
            e.ToTable("demand_observations"); e.HasKey(x => x.Id); e.Property(x => x.Revenue).HasPrecision(19, 4); e.Property(x => x.EffectivePrice).HasPrecision(19, 4); e.Property(x => x.Channel).HasMaxLength(50); e.Property(x => x.Region).HasMaxLength(80); e.HasIndex(x => new { x.VariantId, x.PeriodStart, x.Channel, x.Region }).IsUnique(); e.HasOne<ProductVariant>().WithMany().HasForeignKey(x => x.VariantId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<DemandForecast>(e =>
        {
            e.ToTable("demand_forecasts"); e.HasKey(x => x.Id); e.Property(x => x.PredictedUnits).HasPrecision(19, 4); e.Property(x => x.ConfidenceLower).HasPrecision(19, 4); e.Property(x => x.ConfidenceUpper).HasPrecision(19, 4); e.Property(x => x.ConfidenceLevel).HasPrecision(6, 4); e.Property(x => x.Model).HasConversion<string>().HasMaxLength(40); e.Property(x => x.ModelVersion).HasMaxLength(100); e.Property(x => x.Mae).HasPrecision(19, 4); e.Property(x => x.Rmse).HasPrecision(19, 4); e.Property(x => x.Wape).HasPrecision(19, 4); e.Property(x => x.Bias).HasPrecision(19, 4); e.HasIndex(x => new { x.VariantId, x.GeneratedAt }); e.HasOne<ProductVariant>().WithMany().HasForeignKey(x => x.VariantId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<Warehouse>(e => { e.ToTable("warehouses"); e.HasKey(x => x.Id); e.Property(x => x.Code).HasMaxLength(40); e.Property(x => x.Name).HasMaxLength(160); e.HasIndex(x => x.Code).IsUnique(); });
        model.Entity<InventoryLocation>(e => { e.ToTable("inventory_locations"); e.HasKey(x => x.Id); e.Property(x => x.Zone).HasMaxLength(80); e.Property(x => x.Bin).HasMaxLength(80); e.HasIndex(x => new { x.WarehouseId, x.Zone, x.Bin }).IsUnique(); e.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict); });
        model.Entity<WarehouseStock>(e =>
        {
            e.ToTable("warehouse_stock"); e.HasKey(x => new { x.WarehouseId, x.VariantId }); e.Property(x => x.Version).IsRowVersion(); e.HasIndex(x => new { x.VariantId, x.WarehouseId }); e.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict); e.HasOne<ProductVariant>().WithMany().HasForeignKey(x => x.VariantId).OnDelete(DeleteBehavior.Restrict); e.HasOne<InventoryLocation>().WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict); e.ToTable(t => t.HasCheckConstraint("ck_warehouse_stock_nonnegative", "\"OnHand\" >= 0 AND \"Reserved\" >= 0 AND \"SafetyStock\" >= 0 AND \"Unavailable\" >= 0 AND \"Inbound\" >= 0 AND \"Reserved\" + \"Unavailable\" <= \"OnHand\""));
        });
        model.Entity<InventoryLedgerEntry>(e => { e.ToTable("inventory_ledger"); e.HasKey(x => x.Id); e.Property(x => x.Reason).HasConversion<string>().HasMaxLength(40); e.Property(x => x.State).HasConversion<string>().HasMaxLength(30).HasDefaultValue(InventoryState.Available); e.Property(x => x.ReferenceType).HasMaxLength(80); e.Property(x => x.ReferenceId).HasMaxLength(120); e.Property(x => x.CreatedBy).HasMaxLength(64); e.HasIndex(x => new { x.VariantId, x.WarehouseId, x.CreatedAt }); e.HasOne<ProductVariant>().WithMany().HasForeignKey(x => x.VariantId).OnDelete(DeleteBehavior.Restrict); e.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict); e.HasOne<InventoryLocation>().WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict); e.HasOne<InventoryLot>().WithMany().HasForeignKey(x => x.LotId).OnDelete(DeleteBehavior.Restrict); });
        model.Entity<ReplenishmentRecommendation>(e => { e.ToTable("replenishment_recommendations"); e.HasKey(x => x.Id); e.Property(x => x.AverageDailyDemand).HasPrecision(19, 4); e.Property(x => x.ReorderPoint).HasPrecision(19, 4); e.Property(x => x.ExplanationJson).HasColumnType("jsonb"); e.Property(x => x.ModelVersion).HasMaxLength(100); e.Property(x => x.Status).HasConversion<string>().HasMaxLength(30); e.Property(x => x.ApprovedBy).HasMaxLength(64); e.HasIndex(x => new { x.Status, x.GeneratedAt }); e.HasOne<ProductVariant>().WithMany().HasForeignKey(x => x.VariantId).OnDelete(DeleteBehavior.Restrict); e.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict); });
        model.Entity<OperationalAlert>(e => { e.ToTable("operational_alerts"); e.HasKey(x => x.Id); e.Property(x => x.Type).HasMaxLength(60); e.Property(x => x.DeduplicationKey).HasMaxLength(240); e.Property(x => x.Severity).HasMaxLength(20); e.Property(x => x.Title).HasMaxLength(240); e.Property(x => x.Detail).HasMaxLength(1000); e.Property(x => x.Status).HasConversion<string>().HasMaxLength(30); e.Property(x => x.AcknowledgedBy).HasMaxLength(64); e.HasIndex(x => new { x.DeduplicationKey, x.Status }).IsUnique().HasFilter("\"Status\" <> 'Resolved'"); });
        model.Entity<OperationsAuditEntry>(e => { e.ToTable("operations_audit"); e.HasKey(x => x.Id); e.Property(x => x.EventType).HasMaxLength(80); e.Property(x => x.ResourceType).HasMaxLength(80); e.Property(x => x.ResourceId).HasMaxLength(120); e.Property(x => x.ActorId).HasMaxLength(64); e.Property(x => x.BeforeJson).HasColumnType("jsonb"); e.Property(x => x.AfterJson).HasColumnType("jsonb"); e.Property(x => x.Reason).HasMaxLength(500); e.HasIndex(x => new { x.ResourceType, x.ResourceId, x.CreatedAt }); e.HasIndex(x => x.CreatedAt); });
        model.ConfigureSupplyChain();
        model.ConfigureWarehouseExecution();
    }

    public async Task<IApplicationTransaction> BeginTransactionAsync(CancellationToken cancellationToken) => new ApplicationTransaction(await Database.BeginTransactionAsync(cancellationToken));
    public Task LockCartAsync(Guid cartId, CancellationToken cancellationToken) => Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM carts WHERE \"Id\" = {cartId} FOR UPDATE", cancellationToken);
    public Task LockInventoryAsync(IReadOnlyCollection<Guid> variantIds, CancellationToken cancellationToken) => Database.ExecuteSqlRawAsync("SELECT 1 FROM inventory WHERE \"VariantId\" = ANY ({0}) ORDER BY \"VariantId\" FOR UPDATE", [variantIds.ToArray()], cancellationToken);
    public Task LockPriceActivationAsync(CancellationToken cancellationToken) => Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(6208462301)", cancellationToken);
    public Task LockPurchaseOrderAsync(Guid purchaseOrderId, CancellationToken cancellationToken) => Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM purchase_orders WHERE \"Id\" = {purchaseOrderId} FOR UPDATE", cancellationToken);
    public Task LockReceiptIdempotencyAsync(string key, CancellationToken cancellationToken) => Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 6208462302))", cancellationToken);
    public Task LockRequestForQuotationAsync(Guid rfqId, CancellationToken cancellationToken) => Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM requests_for_quotation WHERE \"Id\" = {rfqId} FOR UPDATE", cancellationToken);
    public Task LockCycleCountAsync(Guid cycleCountId, CancellationToken cancellationToken) => Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM cycle_counts WHERE \"Id\" = {cycleCountId} FOR UPDATE", cancellationToken);
    public Task LockStockTransferAsync(Guid stockTransferId, CancellationToken cancellationToken) => Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM stock_transfers WHERE \"Id\" = {stockTransferId} FOR UPDATE", cancellationToken);
    public Task LockStockTransferIdempotencyAsync(string key, CancellationToken cancellationToken) => Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 6208462303))", cancellationToken);

    private sealed class ApplicationTransaction(IDbContextTransaction transaction) : IApplicationTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken) => transaction.CommitAsync(cancellationToken);
        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
