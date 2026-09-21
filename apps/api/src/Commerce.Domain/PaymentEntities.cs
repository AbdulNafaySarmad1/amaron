namespace Commerce.Domain;

public enum PaymentStatus { Created, Pending, RequiresCustomerAction, Authorized, CapturePending, Captured, PartiallyRefunded, Refunded, Cancelled, Failed, Expired, PendingCollection, Collected, CollectionFailed }
public enum PaymentAttemptStatus { Created, Pending, RequiresCustomerAction, Authorized, Captured, Failed, Cancelled }
public enum PaymentMethodType { Card, BankTransfer, Raast, DigitalWallet, PayPal, CashOnDelivery, Installment, Bnpl, StoreCredit }
public enum PaymentFailureCategory { Declined, AuthenticationRequired, InsufficientFunds, ExpiredMethod, InvalidMethod, ProcessorUnavailable, NetworkError, RiskRejected, LimitExceeded, CancelledByCustomer, Unknown }
public enum RefundStatus { Requested, Processing, Succeeded, Failed, Cancelled }
public enum WebhookProcessingStatus { Received, Processed, Rejected, Failed }
public enum ReconciliationStatus { Matched, MissingInternal, MissingProvider, AmountMismatch, StatusMismatch, RefundMismatch, ManualReview }
public enum InstallmentPlanStatus { Offered, Active, Completed, Failed, Cancelled }
public enum InstallmentScheduleStatus { Scheduled, Due, Processing, Paid, Failed, Cancelled }

public sealed class Payment
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public string? CustomerId { get; set; }
    public string Currency { get; set; } = "USD";
    public decimal AmountAuthorized { get; set; }
    public decimal AmountCaptured { get; set; }
    public decimal AmountRefunded { get; set; }
    public PaymentStatus Status { get; set; }
    public string Provider { get; set; } = "";
    public string? ProviderPaymentReference { get; set; }
    public PaymentMethodType PaymentMethodType { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public Order Order { get; set; } = null!;
    public List<PaymentAttempt> Attempts { get; set; } = [];
    public List<Refund> Refunds { get; set; } = [];
}

public sealed class OrderInventoryReservation
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public Guid WarehouseId { get; set; }
    public Guid VariantId { get; set; }
    public int Quantity { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ReleasedAt { get; set; }
    public DateTimeOffset? FulfilledAt { get; set; }
}

public sealed class PaymentAttempt
{
    public Guid Id { get; set; }
    public Guid PaymentId { get; set; }
    public string Provider { get; set; } = "";
    public string? ProviderReference { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";
    public PaymentMethodType Method { get; set; }
    public PaymentAttemptStatus Status { get; set; }
    public string? FailureCode { get; set; }
    public PaymentFailureCategory? FailureCategory { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string IdempotencyKey { get; set; } = "";
    public bool AuthenticationRequired { get; set; }
    public string? MetadataJson { get; set; }
    public Payment Payment { get; set; } = null!;
}

public sealed class Refund
{
    public Guid Id { get; set; }
    public Guid PaymentId { get; set; }
    public Guid OrderId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";
    public string Reason { get; set; } = "";
    public RefundStatus Status { get; set; }
    public string? ProviderRefundReference { get; set; }
    public string RequestedBy { get; set; } = "";
    public string IdempotencyKey { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public Payment Payment { get; set; } = null!;
}

public sealed class PaymentWebhookEvent
{
    public Guid Id { get; set; }
    public string Provider { get; set; } = "";
    public string ProviderEventId { get; set; } = "";
    public string EventType { get; set; } = "";
    public DateTimeOffset ReceivedAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public WebhookProcessingStatus ProcessingStatus { get; set; }
    public Guid? PaymentId { get; set; }
    public string? FailureCode { get; set; }
    public string PayloadHash { get; set; } = "";
}

public sealed class PaymentReconciliation
{
    public Guid Id { get; set; }
    public Guid PaymentId { get; set; }
    public string Provider { get; set; } = "";
    public ReconciliationStatus Status { get; set; }
    public decimal ProviderCapturedAmount { get; set; }
    public decimal ProviderRefundedAmount { get; set; }
    public string Currency { get; set; } = "USD";
    public string? Detail { get; set; }
    public DateTimeOffset EvaluatedAt { get; set; }
    public string EvaluatedBy { get; set; } = "";
}

public sealed class InstallmentPlan
{
    public Guid Id { get; set; }
    public Guid PaymentId { get; set; }
    public string Provider { get; set; } = "";
    public string? ProviderPlanId { get; set; }
    public string Currency { get; set; } = "USD";
    public decimal TotalAmount { get; set; }
    public int InstallmentCount { get; set; }
    public string Frequency { get; set; } = "Monthly";
    public decimal CustomerCost { get; set; }
    public decimal? MerchantCost { get; set; }
    public InstallmentPlanStatus Status { get; set; }
    public string TermsReference { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public List<InstallmentSchedule> Schedule { get; set; } = [];
}

public sealed class InstallmentSchedule
{
    public Guid Id { get; set; }
    public Guid InstallmentPlanId { get; set; }
    public int Sequence { get; set; }
    public DateTimeOffset DueDate { get; set; }
    public decimal Amount { get; set; }
    public InstallmentScheduleStatus Status { get; set; }
    public string? ProviderPaymentReference { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    public InstallmentPlan Plan { get; set; } = null!;
}
