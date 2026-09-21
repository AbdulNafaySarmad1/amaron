using Commerce.Contracts;
using Commerce.Domain;
using Microsoft.EntityFrameworkCore;

namespace Commerce.Application;

public interface IApplicationTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken);
}

public interface ICommerceDbContext
{
    DbSet<ApplicationUser> ApplicationUsers { get; }
    DbSet<Category> Categories { get; }
    DbSet<Product> Products { get; }
    DbSet<ProductVariant> ProductVariants { get; }
    DbSet<InventoryItem> Inventory { get; }
    DbSet<ProductAsset> ProductAssets { get; }
    DbSet<Review> Reviews { get; }
    DbSet<Cart> Carts { get; }
    DbSet<CartItem> CartItems { get; }
    DbSet<Order> Orders { get; }
    DbSet<OrderItem> OrderItems { get; }
    DbSet<IdempotencyRecord> IdempotencyRecords { get; }
    DbSet<PriceRecord> PriceRecords { get; }
    DbSet<PricingPolicy> PricingPolicies { get; }
    DbSet<PriceRecommendation> PriceRecommendations { get; }
    DbSet<Promotion> Promotions { get; }
    DbSet<DemandObservation> DemandObservations { get; }
    DbSet<DemandForecast> DemandForecasts { get; }
    DbSet<Warehouse> Warehouses { get; }
    DbSet<InventoryLocation> InventoryLocations { get; }
    DbSet<WarehouseStock> WarehouseStocks { get; }
    DbSet<InventoryLedgerEntry> InventoryLedgerEntries { get; }
    DbSet<ReplenishmentRecommendation> ReplenishmentRecommendations { get; }
    DbSet<OperationalAlert> OperationalAlerts { get; }
    DbSet<OperationsAuditEntry> OperationsAuditEntries { get; }
    DbSet<SupplierOrganization> SupplierOrganizations { get; }
    DbSet<SupplierUser> SupplierUsers { get; }
    DbSet<SupplierProductSource> SupplierProductSources { get; }
    DbSet<PurchaseOrder> PurchaseOrders { get; }
    DbSet<PurchaseOrderLine> PurchaseOrderLines { get; }
    DbSet<InboundShipment> InboundShipments { get; }
    DbSet<InboundShipmentLine> InboundShipmentLines { get; }
    DbSet<FiscalInvoice> FiscalInvoices { get; }
    DbSet<FiscalInvoiceLine> FiscalInvoiceLines { get; }
    DbSet<InventoryLot> InventoryLots { get; }
    DbSet<InventoryBalance> InventoryBalances { get; }
    DbSet<GoodsReceipt> GoodsReceipts { get; }
    DbSet<GoodsReceiptLine> GoodsReceiptLines { get; }
    DbSet<InvoiceMatch> InvoiceMatches { get; }
    DbSet<RequestForQuotation> RequestsForQuotation { get; }
    DbSet<RequestForQuotationLine> RequestForQuotationLines { get; }
    DbSet<RequestForQuotationSupplier> RequestForQuotationSuppliers { get; }
    DbSet<SupplierQuotation> SupplierQuotations { get; }
    DbSet<SupplierQuotationLine> SupplierQuotationLines { get; }
    DbSet<WarehouseTask> WarehouseTasks { get; }
    DbSet<CycleCount> CycleCounts { get; }
    DbSet<CycleCountLine> CycleCountLines { get; }
    DbSet<StockTransfer> StockTransfers { get; }
    DbSet<StockTransferLine> StockTransferLines { get; }
    DbSet<Payment> Payments { get; }
    DbSet<PaymentAttempt> PaymentAttempts { get; }
    DbSet<Refund> Refunds { get; }
    DbSet<PaymentWebhookEvent> PaymentWebhookEvents { get; }
    DbSet<PaymentReconciliation> PaymentReconciliations { get; }
    DbSet<InstallmentPlan> InstallmentPlans { get; }
    DbSet<InstallmentSchedule> InstallmentSchedules { get; }
    DbSet<OrderInventoryReservation> OrderInventoryReservations { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
    Task<IApplicationTransaction> BeginTransactionAsync(CancellationToken cancellationToken);
    Task LockCartAsync(Guid cartId, CancellationToken cancellationToken);
    Task LockInventoryAsync(IReadOnlyCollection<Guid> variantIds, CancellationToken cancellationToken);
    Task LockPriceActivationAsync(CancellationToken cancellationToken);
    Task LockPurchaseOrderAsync(Guid purchaseOrderId, CancellationToken cancellationToken);
    Task LockReceiptIdempotencyAsync(string key, CancellationToken cancellationToken);
    Task LockRequestForQuotationAsync(Guid rfqId, CancellationToken cancellationToken);
    Task LockCycleCountAsync(Guid cycleCountId, CancellationToken cancellationToken);
    Task LockStockTransferAsync(Guid stockTransferId, CancellationToken cancellationToken);
    Task LockStockTransferIdempotencyAsync(string key, CancellationToken cancellationToken);
    Task LockPaymentAsync(Guid paymentId, CancellationToken cancellationToken);
    Task LockPaymentIdempotencyAsync(string key, CancellationToken cancellationToken);
    void ClearTracking();
}

public interface IReadModelCache
{
    Task<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T>> factory, TimeSpan expiration, IReadOnlyCollection<string> tags, CancellationToken cancellationToken);
    Task RemoveByTagAsync(string tag, CancellationToken cancellationToken);
}

public interface IInvoiceResolver
{
    bool Supports(string provider);
    void ValidatePayload(string payload);
}

public sealed class CommerceException(string code, string message, int statusCode) : Exception(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
}

public static class CommerceErrors
{
    public static CommerceException NotFound(string resource) => new("resource_not_found", $"{resource} was not found.", 404);
    public static CommerceException Validation(string message) => new("validation_failed", message, 400);
    public static CommerceException Conflict(string code, string message) => new(code, message, 409);
}
