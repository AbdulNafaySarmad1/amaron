using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Commerce.Contracts;
using Commerce.Domain;
using Microsoft.EntityFrameworkCore;

namespace Commerce.Application;

public sealed record PaymentProviderCapabilities(bool SupportsAuthorization, bool SupportsManualCapture, bool SupportsPartialCapture, bool SupportsPartialRefund, bool SupportsInstallments, bool SupportsSavedMethods, bool Supports3Ds, bool SupportsAsyncPayments, IReadOnlyList<PaymentMethodType> Methods);
public sealed record ProviderPaymentRequest(Guid PaymentId, decimal Amount, string Currency, PaymentMethodType Method, string? Token, string IdempotencyKey);
public sealed record ProviderPaymentResult(string Reference, PaymentStatus Status, bool AuthenticationRequired = false, string? FailureCode = null, PaymentFailureCategory? FailureCategory = null);
public sealed record ProviderRefundResult(string Reference, RefundStatus Status, string? FailureCode = null);
public sealed record ProviderPaymentSnapshot(string Reference, PaymentStatus Status, decimal CapturedAmount, decimal RefundedAmount, string Currency);
public sealed record VerifiedWebhook(string EventId, string EventType, string ProviderPaymentReference, PaymentStatus Status, decimal? Amount = null, decimal? RefundedAmount = null);

public interface IPaymentProvider
{
    string Name { get; }
    PaymentProviderCapabilities Capabilities { get; }
    Task<ProviderPaymentResult> CreatePaymentIntentAsync(ProviderPaymentRequest request, CancellationToken cancellationToken);
    Task<ProviderPaymentResult> ConfirmPaymentAsync(string reference, string? token, CancellationToken cancellationToken);
    Task<ProviderPaymentResult> CaptureAsync(string reference, decimal? amount, CancellationToken cancellationToken);
    Task<ProviderPaymentResult> CancelAsync(string reference, CancellationToken cancellationToken);
    Task<ProviderRefundResult> RefundAsync(string reference, decimal amount, string idempotencyKey, CancellationToken cancellationToken);
    Task<ProviderPaymentSnapshot> GetPaymentStatusAsync(string reference, CancellationToken cancellationToken);
    VerifiedWebhook VerifyWebhook(string payload, string? signature);
}

// Local-only deterministic adapter. Production startup rejects it outside Development/Test.
public sealed class TestPaymentProvider(string webhookSecret) : IPaymentProvider
{
    public string Name => "test";
    public PaymentProviderCapabilities Capabilities { get; } = new(true, true, true, true, true, false, true, true, [PaymentMethodType.Card, PaymentMethodType.BankTransfer, PaymentMethodType.Raast, PaymentMethodType.Installment, PaymentMethodType.CashOnDelivery]);
    public Task<ProviderPaymentResult> CreatePaymentIntentAsync(ProviderPaymentRequest request, CancellationToken ct) => Task.FromResult(Result(request.IdempotencyKey, request.Token));
    public Task<ProviderPaymentResult> ConfirmPaymentAsync(string reference, string? token, CancellationToken ct) => Task.FromResult(Result(reference, token));
    public Task<ProviderPaymentResult> CaptureAsync(string reference, decimal? amount, CancellationToken ct) => Task.FromResult(reference.Contains("capture-fail", StringComparison.Ordinal) ? new ProviderPaymentResult(reference, PaymentStatus.Authorized, false, "capture_failed", PaymentFailureCategory.ProcessorUnavailable) : new ProviderPaymentResult(reference, PaymentStatus.Captured));
    public Task<ProviderPaymentResult> CancelAsync(string reference, CancellationToken ct) => Task.FromResult(new ProviderPaymentResult(reference, PaymentStatus.Cancelled));
    public Task<ProviderRefundResult> RefundAsync(string reference, decimal amount, string idempotencyKey, CancellationToken ct) => Task.FromResult(new ProviderRefundResult($"tr_{idempotencyKey}", RefundStatus.Succeeded));
    public Task<ProviderPaymentSnapshot> GetPaymentStatusAsync(string reference, CancellationToken ct) => Task.FromResult(new ProviderPaymentSnapshot(reference, reference.Contains("pending", StringComparison.Ordinal) ? PaymentStatus.Pending : PaymentStatus.Captured, 0, 0, "USD"));
    public VerifiedWebhook VerifyWebhook(string payload, string? signature)
    {
        var expected = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(webhookSecret), Encoding.UTF8.GetBytes(payload)));
        if (string.IsNullOrWhiteSpace(signature) || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(signature))) throw CommerceErrors.Validation("Webhook signature is invalid.");
        var envelope = JsonSerializer.Deserialize<TestWebhook>(payload) ?? throw CommerceErrors.Validation("Webhook payload is invalid.");
        if (!Enum.TryParse<PaymentStatus>(envelope.Status, true, out var status) || string.IsNullOrWhiteSpace(envelope.EventId) || string.IsNullOrWhiteSpace(envelope.Reference)) throw CommerceErrors.Validation("Webhook payload is invalid.");
        return new VerifiedWebhook(envelope.EventId, envelope.EventType ?? "payment.updated", envelope.Reference, status, envelope.Amount, envelope.RefundedAmount);
    }
    private static ProviderPaymentResult Result(string seed, string? token) => token switch { "test:decline" => new($"tp_{seed}", PaymentStatus.Failed, false, "card_declined", PaymentFailureCategory.Declined), "test:requires-action" => new($"tp_{seed}", PaymentStatus.RequiresCustomerAction, true), "test:pending" => new($"tp_{seed}", PaymentStatus.Pending), "test:cod" => new($"tp_{seed}", PaymentStatus.PendingCollection), _ => new($"tp_{seed}", PaymentStatus.Authorized) };
    private sealed record TestWebhook(string EventId, string Reference, string Status, string? EventType, decimal? Amount, decimal? RefundedAmount);
}

public sealed class PaymentOrchestrator(ICommerceDbContext db, IEnumerable<IPaymentProvider> providers, IReadModelCache cache, TimeProvider clock)
{
    public IPaymentProvider Provider(string name) => providers.SingleOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)) ?? throw CommerceErrors.Validation("The selected payment provider is unavailable.");

    public async Task<PaymentDto> StartAttemptAsync(Guid paymentId, string idempotencyKey, string? token, CancellationToken ct)
    {
        ValidateKey(idempotencyKey);
        Payment payment;
        PaymentAttempt attempt;
        await using (var transaction = await db.BeginTransactionAsync(ct))
        {
            await db.LockPaymentIdempotencyAsync($"attempt:{paymentId:N}:{idempotencyKey}", ct);
            await db.LockPaymentAsync(paymentId, ct);
            payment = await db.Payments.Include(x => x.Attempts).SingleOrDefaultAsync(x => x.Id == paymentId, ct) ?? throw CommerceErrors.NotFound("Payment");
            var existing = payment.Attempts.SingleOrDefault(x => x.IdempotencyKey == idempotencyKey);
            if (existing is not null) { await transaction.CommitAsync(ct); return Map(payment); }
            if (payment.Status is PaymentStatus.Captured or PaymentStatus.Refunded or PaymentStatus.Cancelled or PaymentStatus.Expired) throw CommerceErrors.Conflict("payment_state_invalid", "This payment cannot accept another attempt.");
            attempt = new PaymentAttempt { Id = Guid.CreateVersion7(), PaymentId = payment.Id, Provider = payment.Provider, Amount = payment.AmountAuthorized, Currency = payment.Currency, Method = payment.PaymentMethodType, Status = PaymentAttemptStatus.Created, IdempotencyKey = idempotencyKey, CreatedAt = clock.GetUtcNow() };
            payment.Attempts.Add(attempt);
            await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        }
        ProviderPaymentResult result;
        try { result = await Provider(payment.Provider).CreatePaymentIntentAsync(new ProviderPaymentRequest(payment.Id, payment.AmountAuthorized, payment.Currency, payment.PaymentMethodType, token, idempotencyKey), ct); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { result = new ProviderPaymentResult("", PaymentStatus.Pending, false, "provider_timeout", PaymentFailureCategory.NetworkError); }
        return await ApplyAttemptResultAsync(paymentId, attempt.Id, result, ct);
    }

    public async Task<PaymentDto> ConfirmAsync(Guid paymentId, string idempotencyKey, string? token, CancellationToken ct)
    {
        ValidateKey(idempotencyKey);
        var payment = await db.Payments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == paymentId, ct) ?? throw CommerceErrors.NotFound("Payment");
        if (payment.Status != PaymentStatus.RequiresCustomerAction) throw CommerceErrors.Conflict("payment_state_invalid", "This payment does not require customer action.");
        var result = await Provider(payment.Provider).ConfirmPaymentAsync(payment.ProviderPaymentReference ?? throw CommerceErrors.Conflict("provider_reference_missing", "Payment provider reference is missing."), token, ct);
        return await ApplyAttemptResultAsync(paymentId, null, result, ct);
    }

    public async Task<PaymentDto> CaptureAsync(Guid paymentId, decimal? amount, string actor, CancellationToken ct)
    {
        await using var transaction = await db.BeginTransactionAsync(ct); await db.LockPaymentAsync(paymentId, ct);
        var payment = await db.Payments.Include(x => x.Order).Include(x => x.Attempts).SingleOrDefaultAsync(x => x.Id == paymentId, ct) ?? throw CommerceErrors.NotFound("Payment");
        if (payment.Status != PaymentStatus.Authorized) throw CommerceErrors.Conflict("payment_state_invalid", "Only authorized payments can be captured.");
        var captureAmount = amount ?? payment.AmountAuthorized;
        if (captureAmount <= 0 || captureAmount > payment.AmountAuthorized - payment.AmountCaptured) throw CommerceErrors.Validation("Capture amount is invalid.");
        payment.Status = PaymentStatus.CapturePending; payment.UpdatedAt = clock.GetUtcNow(); await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        var result = await Provider(payment.Provider).CaptureAsync(payment.ProviderPaymentReference!, captureAmount, ct);
        return await ApplyCaptureResultAsync(paymentId, captureAmount, result, actor, ct);
    }

    public async Task<RefundDto> RefundAsync(Guid paymentId, RefundRequest request, string idempotencyKey, string actor, CancellationToken ct)
    {
        ValidateKey(idempotencyKey);
        Payment payment; Refund refund;
        await using (var transaction = await db.BeginTransactionAsync(ct))
        {
            await db.LockPaymentIdempotencyAsync($"refund:{paymentId:N}:{idempotencyKey}", ct); await db.LockPaymentAsync(paymentId, ct);
            payment = await db.Payments.SingleOrDefaultAsync(x => x.Id == paymentId, ct) ?? throw CommerceErrors.NotFound("Payment");
            var existing = await db.Refunds.SingleOrDefaultAsync(x => x.PaymentId == paymentId && x.IdempotencyKey == idempotencyKey, ct);
            if (existing is not null) { await transaction.CommitAsync(ct); return Map(existing); }
            if (request.Amount <= 0 || request.Amount > payment.AmountCaptured - payment.AmountRefunded) throw CommerceErrors.Conflict("refund_amount_invalid", "The refund exceeds the captured amount remaining.");
            refund = new Refund { Id = Guid.CreateVersion7(), PaymentId = payment.Id, OrderId = payment.OrderId, Amount = request.Amount, Currency = payment.Currency, Reason = request.Reason.Trim(), Status = RefundStatus.Processing, RequestedBy = actor, IdempotencyKey = idempotencyKey, CreatedAt = clock.GetUtcNow() };
            db.Refunds.Add(refund); await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        }
        var result = await Provider(payment.Provider).RefundAsync(payment.ProviderPaymentReference!, refund.Amount, idempotencyKey, ct);
        await using var finalize = await db.BeginTransactionAsync(ct); await db.LockPaymentAsync(paymentId, ct);
        payment = await db.Payments.SingleAsync(x => x.Id == paymentId, ct); refund = await db.Refunds.SingleAsync(x => x.Id == refund.Id, ct);
        refund.Status = result.Status; refund.ProviderRefundReference = result.Reference; refund.CompletedAt = clock.GetUtcNow();
        if (result.Status == RefundStatus.Succeeded) { payment.AmountRefunded += refund.Amount; payment.Status = payment.AmountRefunded == payment.AmountCaptured ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded; payment.UpdatedAt = clock.GetUtcNow(); }
        Audit("PAYMENT_REFUND_REQUESTED", payment.Id, actor, $"{refund.Amount} {payment.Currency}: {refund.Reason}"); await db.SaveChangesAsync(ct); await finalize.CommitAsync(ct); return Map(refund);
    }

    public async Task HandleWebhookAsync(string providerName, string payload, string? signature, CancellationToken ct)
    {
        var verified = Provider(providerName).VerifyWebhook(payload, signature);
        await using var transaction = await db.BeginTransactionAsync(ct); await db.LockPaymentIdempotencyAsync($"webhook:{providerName}:{verified.EventId}", ct);
        if (await db.PaymentWebhookEvents.AnyAsync(x => x.Provider == providerName && x.ProviderEventId == verified.EventId, ct)) { await transaction.CommitAsync(ct); return; }
        var payment = await db.Payments.Include(x => x.Attempts).SingleOrDefaultAsync(x => x.Provider == providerName && x.ProviderPaymentReference == verified.ProviderPaymentReference, ct);
        var evt = new PaymentWebhookEvent { Id = Guid.CreateVersion7(), Provider = providerName, ProviderEventId = verified.EventId, EventType = verified.EventType, ReceivedAt = clock.GetUtcNow(), PaymentId = payment?.Id, ProcessingStatus = payment is null ? WebhookProcessingStatus.Rejected : WebhookProcessingStatus.Received, PayloadHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))) };
        db.PaymentWebhookEvents.Add(evt);
        if (payment is not null && CanTransition(payment.Status, verified.Status)) ApplyStatus(payment, verified.Status, verified.Amount, clock.GetUtcNow());
        evt.ProcessingStatus = payment is null ? WebhookProcessingStatus.Rejected : WebhookProcessingStatus.Processed; evt.ProcessedAt = clock.GetUtcNow(); await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        if (payment?.Status == PaymentStatus.Captured) await FulfillReservationAsync(payment.Id, "webhook", ct);
    }

    public async Task<IReadOnlyList<PaymentDto>> GetPaymentsAsync(CancellationToken ct) => (await db.Payments.AsNoTracking().Include(x => x.Attempts).OrderByDescending(x => x.CreatedAt).Take(300).ToListAsync(ct)).Select(Map).ToList();
    public async Task<PaymentDto> GetPaymentAsync(Guid id, CancellationToken ct) => Map(await db.Payments.AsNoTracking().Include(x => x.Attempts).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw CommerceErrors.NotFound("Payment"));
    public IReadOnlyList<PaymentProviderCapabilityDto> GetProviders() => providers.Select(x => new PaymentProviderCapabilityDto(x.Name, x.Capabilities.SupportsAuthorization, x.Capabilities.SupportsManualCapture, x.Capabilities.SupportsPartialCapture, x.Capabilities.SupportsPartialRefund, x.Capabilities.SupportsInstallments, x.Capabilities.SupportsSavedMethods, x.Capabilities.Supports3Ds, x.Capabilities.SupportsAsyncPayments, x.Capabilities.Methods.Select(m => m.ToString()).ToList())).ToList();
    public async Task<ReconciliationDto> ReconcileAsync(Guid paymentId, string actor, CancellationToken ct) { var payment = await db.Payments.SingleOrDefaultAsync(x => x.Id == paymentId, ct) ?? throw CommerceErrors.NotFound("Payment"); var snapshot = await Provider(payment.Provider).GetPaymentStatusAsync(payment.ProviderPaymentReference!, ct); var status = snapshot.Status == payment.Status && snapshot.CapturedAmount == payment.AmountCaptured && snapshot.RefundedAmount == payment.AmountRefunded ? ReconciliationStatus.Matched : snapshot.Status != payment.Status ? ReconciliationStatus.StatusMismatch : snapshot.CapturedAmount != payment.AmountCaptured ? ReconciliationStatus.AmountMismatch : ReconciliationStatus.RefundMismatch; var row = new PaymentReconciliation { Id = Guid.CreateVersion7(), PaymentId = payment.Id, Provider = payment.Provider, Status = status, ProviderCapturedAmount = snapshot.CapturedAmount, ProviderRefundedAmount = snapshot.RefundedAmount, Currency = payment.Currency, EvaluatedAt = clock.GetUtcNow(), EvaluatedBy = actor }; db.PaymentReconciliations.Add(row); Audit("PAYMENT_RECONCILED", payment.Id, actor, status.ToString()); await db.SaveChangesAsync(ct); return new(row.Id, row.PaymentId, row.Provider, row.Status.ToString(), new(row.ProviderCapturedAmount, row.Currency), new(row.ProviderRefundedAmount, row.Currency), row.Detail, row.EvaluatedAt); }

    private async Task<PaymentDto> ApplyAttemptResultAsync(Guid paymentId, Guid? attemptId, ProviderPaymentResult result, CancellationToken ct) { await using var transaction = await db.BeginTransactionAsync(ct); await db.LockPaymentAsync(paymentId, ct); var payment = await db.Payments.Include(x => x.Attempts).Include(x => x.Order).SingleAsync(x => x.Id == paymentId, ct); var attempt = attemptId is null ? payment.Attempts.OrderByDescending(x => x.CreatedAt).First() : payment.Attempts.Single(x => x.Id == attemptId); attempt.ProviderReference = result.Reference; attempt.AuthenticationRequired = result.AuthenticationRequired; attempt.FailureCode = result.FailureCode; attempt.FailureCategory = result.FailureCategory; attempt.Status = ToAttemptStatus(result.Status); attempt.CompletedAt = result.Status is PaymentStatus.Pending or PaymentStatus.RequiresCustomerAction ? null : clock.GetUtcNow(); payment.ProviderPaymentReference = string.IsNullOrEmpty(result.Reference) ? payment.ProviderPaymentReference : result.Reference; ApplyStatus(payment, result.Status, payment.AmountAuthorized, clock.GetUtcNow()); if (payment.Status == PaymentStatus.Authorized && payment.Order.Status == OrderStatus.PendingPayment) payment.Order.Status = OrderStatus.Confirmed; await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); if (payment.Status == PaymentStatus.Captured) await FulfillReservationAsync(payment.Id, "payment", ct); return Map(payment); }
    private async Task<PaymentDto> ApplyCaptureResultAsync(Guid paymentId, decimal amount, ProviderPaymentResult result, string actor, CancellationToken ct) { await using var transaction = await db.BeginTransactionAsync(ct); await db.LockPaymentAsync(paymentId, ct); var payment = await db.Payments.Include(x => x.Attempts).SingleAsync(x => x.Id == paymentId, ct); if (result.FailureCategory is not null) { payment.Status = PaymentStatus.Authorized; payment.UpdatedAt = clock.GetUtcNow(); } else { payment.AmountCaptured += amount; payment.Status = payment.AmountCaptured == payment.AmountAuthorized ? PaymentStatus.Captured : PaymentStatus.Authorized; payment.UpdatedAt = clock.GetUtcNow(); } Audit("PAYMENT_CAPTURED", payment.Id, actor, $"{amount} {payment.Currency}"); await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); if (payment.Status == PaymentStatus.Captured) await FulfillReservationAsync(payment.Id, actor, ct); return Map(payment); }
    private async Task FulfillReservationAsync(Guid paymentId, string actor, CancellationToken ct) { await using var transaction = await db.BeginTransactionAsync(ct); await db.LockPaymentAsync(paymentId, ct); var payment = await db.Payments.Include(x => x.Order).SingleAsync(x => x.Id == paymentId, ct); var rows = await db.OrderInventoryReservations.Where(x => x.OrderId == payment.OrderId && x.FulfilledAt == null && x.ReleasedAt == null).ToListAsync(ct); if (rows.Count == 0) { await transaction.CommitAsync(ct); return; } var variants = rows.Select(x => x.VariantId).Distinct().Order().ToArray(); await db.LockInventoryAsync(variants, ct); var inventory = await db.Inventory.Where(x => variants.Contains(x.VariantId)).ToDictionaryAsync(x => x.VariantId, ct); var stocks = await db.WarehouseStocks.Where(x => rows.Select(r => r.WarehouseId).Contains(x.WarehouseId) && variants.Contains(x.VariantId)).ToDictionaryAsync(x => (x.WarehouseId, x.VariantId), ct); var now = clock.GetUtcNow(); foreach (var row in rows) { var stock = stocks[(row.WarehouseId, row.VariantId)]; var consumed = await InventoryBalanceOperations.ConsumeAvailableAsync(db, stock, row.Quantity, now, ct); stock.Reserved -= row.Quantity; stock.OnHand -= row.Quantity; stock.UpdatedAt = now; inventory[row.VariantId].QuantityOnHand -= row.Quantity; inventory[row.VariantId].UpdatedAt = now; row.FulfilledAt = now; foreach (var allocation in consumed) db.InventoryLedgerEntries.Add(new InventoryLedgerEntry { Id = Guid.CreateVersion7(), VariantId = row.VariantId, WarehouseId = row.WarehouseId, LocationId = allocation.Balance.LocationId, LotId = allocation.Balance.LotId, State = InventoryState.Available, QuantityDelta = -allocation.Quantity, Reason = InventoryMovementReason.OrderFulfilled, ReferenceType = "Order", ReferenceId = payment.OrderId.ToString(), CreatedBy = actor, CreatedAt = now }); } payment.Order.Status = OrderStatus.Confirmed; await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); await cache.RemoveByTagAsync("products", CancellationToken.None); await cache.RemoveByTagAsync("homepage", CancellationToken.None); }
    private static bool CanTransition(PaymentStatus current, PaymentStatus next) => current == next || current switch { PaymentStatus.Created or PaymentStatus.Pending or PaymentStatus.RequiresCustomerAction => next is PaymentStatus.Pending or PaymentStatus.RequiresCustomerAction or PaymentStatus.Authorized or PaymentStatus.Captured or PaymentStatus.Failed or PaymentStatus.Cancelled, PaymentStatus.Authorized or PaymentStatus.CapturePending => next is PaymentStatus.Authorized or PaymentStatus.Captured or PaymentStatus.Cancelled, PaymentStatus.Captured or PaymentStatus.PartiallyRefunded => next is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded, _ => false };
    private static void ApplyStatus(Payment payment, PaymentStatus status, decimal? amount, DateTimeOffset now) { if (!CanTransition(payment.Status, status)) return; payment.Status = status; if (status == PaymentStatus.Authorized) payment.AmountAuthorized = amount ?? payment.AmountAuthorized; if (status == PaymentStatus.Captured) payment.AmountCaptured = amount ?? payment.AmountAuthorized; payment.UpdatedAt = now; }
    private static PaymentAttemptStatus ToAttemptStatus(PaymentStatus status) => status switch { PaymentStatus.RequiresCustomerAction => PaymentAttemptStatus.RequiresCustomerAction, PaymentStatus.Authorized => PaymentAttemptStatus.Authorized, PaymentStatus.Captured => PaymentAttemptStatus.Captured, PaymentStatus.Failed => PaymentAttemptStatus.Failed, PaymentStatus.Cancelled => PaymentAttemptStatus.Cancelled, PaymentStatus.Pending => PaymentAttemptStatus.Pending, _ => PaymentAttemptStatus.Created };
    private static PaymentDto Map(Payment x) => new(x.Id, x.OrderId, x.CustomerId, new(x.AmountAuthorized, x.Currency), new(x.AmountCaptured, x.Currency), new(x.AmountRefunded, x.Currency), x.Status.ToString(), x.Provider, x.ProviderPaymentReference, x.PaymentMethodType.ToString(), x.CreatedAt, x.UpdatedAt, x.ExpiresAt, x.Attempts.OrderBy(x => x.CreatedAt).Select(a => new PaymentAttemptDto(a.Id, a.Provider, a.ProviderReference, new(a.Amount, a.Currency), a.Method.ToString(), a.Status.ToString(), a.FailureCategory?.ToString(), a.AuthenticationRequired, a.CreatedAt, a.CompletedAt)).ToList());
    private static RefundDto Map(Refund x) => new(x.Id, x.PaymentId, x.OrderId, new(x.Amount, x.Currency), x.Reason, x.Status.ToString(), x.ProviderRefundReference, x.RequestedBy, x.CreatedAt, x.CompletedAt);
    private void Audit(string eventType, Guid paymentId, string actor, string reason) => db.OperationsAuditEntries.Add(new OperationsAuditEntry { Id = Guid.CreateVersion7(), EventType = eventType, ResourceType = "Payment", ResourceId = paymentId.ToString(), ActorId = actor, Reason = reason[..Math.Min(500, reason.Length)], CreatedAt = clock.GetUtcNow() });
    private static void ValidateKey(string key) { if (string.IsNullOrWhiteSpace(key) || key.Length > 128 || key.Any(char.IsControl)) throw CommerceErrors.Validation("A valid Idempotency-Key header is required."); }
}
