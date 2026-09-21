namespace Commerce.Domain;

public enum SupplierStatus { Prospective, Active, Suspended, Archived }
public enum PurchaseOrderStatus { Draft, Approved, Sent, PartiallyReceived, Received, Cancelled }
public enum InboundShipmentStatus { Draft, Submitted, InTransit, Arrived, Closed, Cancelled }
public enum FiscalInvoiceStatus { Ingested, Matched, Exception, Rejected }
public enum GoodsReceiptStatus { Posted, Reversed }
public enum InventoryState { Available, Reserved, Allocated, Inbound, InTransit, Quarantined, Damaged, Expired, PickFace, Reserve }
public enum MatchStatus { Pending, Matched, Exception }
public enum RequestForQuotationStatus { Draft, Open, Closed, Awarded, Cancelled }
public enum SupplierQuotationStatus { Submitted, Awarded, Rejected, Withdrawn }

public sealed class SupplierOrganization
{
    public Guid Id { get; set; }
    public string Code { get; set; } = "";
    public string LegalName { get; set; } = "";
    public string CountryCode { get; set; } = "";
    public string? TaxIdentifier { get; set; }
    public SupplierStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public uint Version { get; set; }
}

public sealed class SupplierUser
{
    public Guid SupplierOrganizationId { get; set; }
    public Guid ApplicationUserId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class SupplierProductSource
{
    public Guid Id { get; set; }
    public Guid SupplierOrganizationId { get; set; }
    public Guid VariantId { get; set; }
    public string SupplierSku { get; set; } = "";
    public string Currency { get; set; } = "USD";
    public decimal UnitCost { get; set; }
    public int LeadTimeDays { get; set; }
    public int MinimumOrderQuantity { get; set; } = 1;
    public int OrderMultiple { get; set; } = 1;
    public decimal? ReliabilityPercent { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class RequestForQuotation
{
    public Guid Id { get; set; }
    public string Number { get; set; } = "";
    public Guid WarehouseId { get; set; }
    public RequestForQuotationStatus Status { get; set; }
    public string Currency { get; set; } = "USD";
    public DateTimeOffset ResponseDeadline { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? AwardedQuotationId { get; set; }
    public string? AwardReason { get; set; }
    public string? AwardedBy { get; set; }
    public DateTimeOffset? AwardedAt { get; set; }
    public uint Version { get; set; }
    public List<RequestForQuotationLine> Lines { get; set; } = [];
    public List<RequestForQuotationSupplier> Suppliers { get; set; } = [];
}

public sealed class RequestForQuotationLine
{
    public Guid Id { get; set; }
    public Guid RequestForQuotationId { get; set; }
    public Guid VariantId { get; set; }
    public int Quantity { get; set; }
    public RequestForQuotation RequestForQuotation { get; set; } = null!;
}

public sealed class RequestForQuotationSupplier
{
    public Guid RequestForQuotationId { get; set; }
    public Guid SupplierOrganizationId { get; set; }
    public DateTimeOffset InvitedAt { get; set; }
}

public sealed class SupplierQuotation
{
    public Guid Id { get; set; }
    public string Number { get; set; } = "";
    public Guid RequestForQuotationId { get; set; }
    public Guid SupplierOrganizationId { get; set; }
    public string Currency { get; set; } = "USD";
    public SupplierQuotationStatus Status { get; set; }
    public string RequestHash { get; set; } = "";
    public string SubmittedBy { get; set; } = "";
    public DateTimeOffset SubmittedAt { get; set; }
    public List<SupplierQuotationLine> Lines { get; set; } = [];
}

public sealed class SupplierQuotationLine
{
    public Guid Id { get; set; }
    public Guid SupplierQuotationId { get; set; }
    public Guid RequestForQuotationId { get; set; }
    public Guid RequestForQuotationLineId { get; set; }
    public string SupplierSku { get; set; } = "";
    public decimal UnitCost { get; set; }
    public int LeadTimeDays { get; set; }
    public int MinimumOrderQuantity { get; set; } = 1;
    public int OrderMultiple { get; set; } = 1;
    public decimal? ReliabilityPercent { get; set; }
    public SupplierQuotation SupplierQuotation { get; set; } = null!;
    public RequestForQuotationLine RequestForQuotationLine { get; set; } = null!;
}

public sealed class PurchaseOrder
{
    public Guid Id { get; set; }
    public string Number { get; set; } = "";
    public Guid SupplierOrganizationId { get; set; }
    public Guid WarehouseId { get; set; }
    public Guid? SourceQuotationId { get; set; }
    public int Revision { get; set; } = 1;
    public PurchaseOrderStatus Status { get; set; }
    public string Currency { get; set; } = "USD";
    public DateTimeOffset ExpectedAt { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public uint Version { get; set; }
    public List<PurchaseOrderLine> Lines { get; set; } = [];
}

public sealed class PurchaseOrderLine
{
    public Guid Id { get; set; }
    public Guid PurchaseOrderId { get; set; }
    public Guid VariantId { get; set; }
    public Guid? SourceQuotationLineId { get; set; }
    public string SupplierSku { get; set; } = "";
    public int OrderedQuantity { get; set; }
    public int ReceivedQuantity { get; set; }
    public decimal UnitCost { get; set; }
    public int LeadTimeDays { get; set; }
    public int MinimumOrderQuantity { get; set; } = 1;
    public int OrderMultiple { get; set; } = 1;
    public decimal? ReliabilityPercent { get; set; }
    public PurchaseOrder PurchaseOrder { get; set; } = null!;
}

public sealed class InboundShipment
{
    public Guid Id { get; set; }
    public string Number { get; set; } = "";
    public Guid SupplierOrganizationId { get; set; }
    public Guid PurchaseOrderId { get; set; }
    public InboundShipmentStatus Status { get; set; }
    public string? Carrier { get; set; }
    public string? TrackingNumber { get; set; }
    public DateTimeOffset? EstimatedArrival { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string IdempotencyKey { get; set; } = "";
    public string RequestHash { get; set; } = "";
    public List<InboundShipmentLine> Lines { get; set; } = [];
}

public sealed class InboundShipmentLine
{
    public Guid Id { get; set; }
    public Guid InboundShipmentId { get; set; }
    public Guid PurchaseOrderLineId { get; set; }
    public Guid VariantId { get; set; }
    public int ExpectedQuantity { get; set; }
    public InboundShipment InboundShipment { get; set; } = null!;
}

public sealed class FiscalInvoice
{
    public Guid Id { get; set; }
    public Guid SupplierOrganizationId { get; set; }
    public Guid? PurchaseOrderId { get; set; }
    public string Provider { get; set; } = "";
    public string ExternalNumber { get; set; } = "";
    public string Currency { get; set; } = "USD";
    public decimal TotalAmount { get; set; }
    public FiscalInvoiceStatus Status { get; set; }
    public string PayloadHash { get; set; } = "";
    public string RequestHash { get; set; } = "";
    public string? DocumentReference { get; set; }
    public DateTimeOffset IssuedAt { get; set; }
    public DateTimeOffset IngestedAt { get; set; }
    public List<FiscalInvoiceLine> Lines { get; set; } = [];
}

public sealed class FiscalInvoiceLine
{
    public Guid Id { get; set; }
    public Guid FiscalInvoiceId { get; set; }
    public Guid? PurchaseOrderLineId { get; set; }
    public Guid? VariantId { get; set; }
    public string SupplierSku { get; set; } = "";
    public int Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public FiscalInvoice FiscalInvoice { get; set; } = null!;
}

public sealed class InventoryLot
{
    public Guid Id { get; set; }
    public Guid VariantId { get; set; }
    public string LotNumber { get; set; } = "";
    public DateTimeOffset? ManufacturedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class InventoryBalance
{
    public Guid Id { get; set; }
    public Guid WarehouseId { get; set; }
    public Guid? LocationId { get; set; }
    public Guid VariantId { get; set; }
    public Guid? LotId { get; set; }
    public InventoryState State { get; set; }
    public int Quantity { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public uint Version { get; set; }
}

public sealed class GoodsReceipt
{
    public Guid Id { get; set; }
    public string Number { get; set; } = "";
    public Guid PurchaseOrderId { get; set; }
    public Guid? InboundShipmentId { get; set; }
    public Guid WarehouseId { get; set; }
    public string IdempotencyKey { get; set; } = "";
    public string RequestHash { get; set; } = "";
    public GoodsReceiptStatus Status { get; set; }
    public string ReceivedBy { get; set; } = "";
    public DateTimeOffset ReceivedAt { get; set; }
    public List<GoodsReceiptLine> Lines { get; set; } = [];
}

public sealed class GoodsReceiptLine
{
    public Guid Id { get; set; }
    public Guid GoodsReceiptId { get; set; }
    public Guid PurchaseOrderLineId { get; set; }
    public Guid VariantId { get; set; }
    public Guid? LotId { get; set; }
    public int ExpectedQuantity { get; set; }
    public int PhysicalQuantity { get; set; }
    public int AcceptedQuantity { get; set; }
    public int DamagedQuantity { get; set; }
    public int QuarantinedQuantity { get; set; }
    public int MissingQuantity { get; set; }
    public GoodsReceipt GoodsReceipt { get; set; } = null!;
}

public sealed class InvoiceMatch
{
    public Guid Id { get; set; }
    public Guid PurchaseOrderId { get; set; }
    public Guid FiscalInvoiceId { get; set; }
    public MatchStatus Status { get; set; }
    public string ExceptionsJson { get; set; } = "[]";
    public DateTimeOffset EvaluatedAt { get; set; }
}
