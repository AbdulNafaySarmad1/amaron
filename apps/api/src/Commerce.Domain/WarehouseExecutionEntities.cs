namespace Commerce.Domain;

public enum WarehouseTaskType { Putaway, Replenishment, Pick, CycleCount, Transfer }
public enum WarehouseTaskStatus { Open, Assigned, InProgress, Completed, Cancelled }
public enum CycleCountStatus { Planned, InProgress, Submitted, Reconciled, Cancelled }
public enum StockTransferStatus { Draft, InTransit, Received, Cancelled }

public sealed class WarehouseTask
{
    public Guid Id { get; set; }
    public string Number { get; set; } = "";
    public Guid WarehouseId { get; set; }
    public WarehouseTaskType Type { get; set; }
    public WarehouseTaskStatus Status { get; set; }
    public int Priority { get; set; }
    public Guid? SourceLocationId { get; set; }
    public Guid? DestinationLocationId { get; set; }
    public Guid VariantId { get; set; }
    public Guid? LotId { get; set; }
    public InventoryState InventoryState { get; set; }
    public int Quantity { get; set; }
    public int CompletedQuantity { get; set; }
    public string ReferenceType { get; set; } = "";
    public string ReferenceId { get; set; } = "";
    public string? AssignedTo { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public uint Version { get; set; }
}

public sealed class CycleCount
{
    public Guid Id { get; set; }
    public string Number { get; set; } = "";
    public Guid WarehouseId { get; set; }
    public CycleCountStatus Status { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public string? CountedBy { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public string? ReconciledBy { get; set; }
    public DateTimeOffset? ReconciledAt { get; set; }
    public uint Version { get; set; }
    public List<CycleCountLine> Lines { get; set; } = [];
}

public sealed class CycleCountLine
{
    public Guid Id { get; set; }
    public Guid CycleCountId { get; set; }
    public Guid VariantId { get; set; }
    public Guid? LocationId { get; set; }
    public Guid? LotId { get; set; }
    public InventoryState State { get; set; }
    public int ExpectedQuantity { get; set; }
    public int? CountedQuantity { get; set; }
    public CycleCount CycleCount { get; set; } = null!;
}

public sealed class StockTransfer
{
    public Guid Id { get; set; }
    public string Number { get; set; } = "";
    public Guid FromWarehouseId { get; set; }
    public Guid ToWarehouseId { get; set; }
    public StockTransferStatus Status { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public string CreateIdempotencyKey { get; set; } = "";
    public string CreateRequestHash { get; set; } = "";
    public string? DispatchedBy { get; set; }
    public DateTimeOffset? DispatchedAt { get; set; }
    public string? DispatchIdempotencyKey { get; set; }
    public string? ReceivedBy { get; set; }
    public DateTimeOffset? ReceivedAt { get; set; }
    public string? ReceiveIdempotencyKey { get; set; }
    public uint Version { get; set; }
    public List<StockTransferLine> Lines { get; set; } = [];
}

public sealed class StockTransferLine
{
    public Guid Id { get; set; }
    public Guid StockTransferId { get; set; }
    public Guid VariantId { get; set; }
    public Guid? LotId { get; set; }
    public int Quantity { get; set; }
    public StockTransfer StockTransfer { get; set; } = null!;
}
