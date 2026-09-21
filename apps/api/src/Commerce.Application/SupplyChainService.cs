using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Commerce.Contracts;
using Commerce.Domain;
using Microsoft.EntityFrameworkCore;

namespace Commerce.Application;

public sealed class SupplyChainService(ICommerceDbContext db, TimeProvider clock, IEnumerable<IInvoiceResolver> invoiceResolvers)
{
    public async Task<IReadOnlyList<SupplierDto>> GetSuppliersAsync(CancellationToken ct) =>
        await db.SupplierOrganizations.AsNoTracking().OrderBy(x => x.LegalName).Select(x => new SupplierDto(x.Id, x.Code, x.LegalName, x.CountryCode, x.TaxIdentifier, x.Status.ToString(), x.CreatedAt)).ToListAsync(ct);

    public async Task<SupplierDto> CreateSupplierAsync(CreateSupplierRequest request, string actor, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Code) || request.Code.Length > 40 || string.IsNullOrWhiteSpace(request.LegalName) || request.LegalName.Length > 240 || request.CountryCode.Trim().Length != 2)
            throw CommerceErrors.Validation("Supplier code, legal name, and ISO country code are required.");
        var code = request.Code.Trim().ToUpperInvariant();
        if (await db.SupplierOrganizations.AnyAsync(x => x.Code == code, ct)) throw CommerceErrors.Conflict("supplier_code_exists", "A supplier with this code already exists.");
        var supplier = new SupplierOrganization { Id = Guid.CreateVersion7(), Code = code, LegalName = request.LegalName.Trim(), CountryCode = request.CountryCode.Trim().ToUpperInvariant(), TaxIdentifier = Limited(request.TaxIdentifier, 80), Status = SupplierStatus.Active, CreatedAt = clock.GetUtcNow() };
        db.SupplierOrganizations.Add(supplier); Audit("SUPPLIER_CREATED", "SupplierOrganization", supplier.Id, actor, request.LegalName); await db.SaveChangesAsync(ct);
        return MapSupplier(supplier);
    }

    public async Task AssignSupplierUserAsync(Guid supplierId, AssignSupplierUserRequest request, string actor, CancellationToken ct)
    {
        if (!await db.SupplierOrganizations.AnyAsync(x => x.Id == supplierId && x.Status == SupplierStatus.Active, ct)) throw CommerceErrors.NotFound("Active supplier");
        if (!await db.ApplicationUsers.AnyAsync(x => x.Id == request.ApplicationUserId, ct)) throw CommerceErrors.NotFound("Application user");
        var membership = await db.SupplierUsers.SingleOrDefaultAsync(x => x.SupplierOrganizationId == supplierId && x.ApplicationUserId == request.ApplicationUserId, ct);
        if (membership is null) db.SupplierUsers.Add(new SupplierUser { SupplierOrganizationId = supplierId, ApplicationUserId = request.ApplicationUserId, CreatedAt = clock.GetUtcNow() }); else membership.IsActive = true;
        Audit("SUPPLIER_USER_ASSIGNED", "SupplierOrganization", supplierId, actor, request.ApplicationUserId.ToString()); await db.SaveChangesAsync(ct);
    }

    public async Task<SupplierSourceDto> CreateSourceAsync(SupplierSourceRequest request, string actor, CancellationToken ct)
    {
        if (!ValidMoney(request.UnitCost) || request.LeadTimeDays < 0 || request.MinimumOrderQuantity < 1 || request.OrderMultiple < 1 || !ValidPercent(request.ReliabilityPercent) || request.Currency.Trim().Length != 3 || string.IsNullOrWhiteSpace(request.SupplierSku)) throw CommerceErrors.Validation("Supplier source terms are invalid.");
        if (!await db.SupplierOrganizations.AnyAsync(x => x.Id == request.SupplierOrganizationId && x.Status == SupplierStatus.Active, ct)) throw CommerceErrors.NotFound("Active supplier");
        if (!await db.ProductVariants.AnyAsync(x => x.Id == request.VariantId, ct)) throw CommerceErrors.NotFound("Variant");
        var source = new SupplierProductSource { Id = Guid.CreateVersion7(), SupplierOrganizationId = request.SupplierOrganizationId, VariantId = request.VariantId, SupplierSku = request.SupplierSku.Trim(), Currency = request.Currency.Trim().ToUpperInvariant(), UnitCost = request.UnitCost, LeadTimeDays = request.LeadTimeDays, MinimumOrderQuantity = request.MinimumOrderQuantity, OrderMultiple = request.OrderMultiple, ReliabilityPercent = request.ReliabilityPercent, UpdatedAt = clock.GetUtcNow() };
        db.SupplierProductSources.Add(source); Audit("SUPPLIER_SOURCE_CREATED", "SupplierProductSource", source.Id, actor, source.SupplierSku); await db.SaveChangesAsync(ct);
        return MapSource(source);
    }

    public async Task<IReadOnlyList<SupplierSourceDto>> GetSourcesAsync(Guid? variantId, CancellationToken ct)
    {
        var query = db.SupplierProductSources.AsNoTracking().Where(x => x.IsActive);
        if (variantId is not null) query = query.Where(x => x.VariantId == variantId);
        return (await query.OrderBy(x => x.UnitCost).ThenBy(x => x.LeadTimeDays).ToListAsync(ct)).Select(MapSource).ToList();
    }

    public async Task<RfqDto> CreateRfqAsync(CreateRfqRequest request, string actor, CancellationToken ct)
    {
        if (request.Currency.Trim().Length != 3 || request.ResponseDeadline <= clock.GetUtcNow() || request.SupplierOrganizationIds.Count is < 1 or > 100 || request.Lines.Count is < 1 or > 200 || request.SupplierOrganizationIds.Distinct().Count() != request.SupplierOrganizationIds.Count || request.Lines.Select(x => x.VariantId).Distinct().Count() != request.Lines.Count || request.Lines.Any(x => x.Quantity < 1)) throw CommerceErrors.Validation("RFQ scope, deadline, suppliers, or lines are invalid.");
        if (!await db.Warehouses.AnyAsync(x => x.Id == request.WarehouseId && x.IsActive, ct)) throw CommerceErrors.NotFound("Warehouse");
        if (await db.SupplierOrganizations.CountAsync(x => request.SupplierOrganizationIds.Contains(x.Id) && x.Status == SupplierStatus.Active, ct) != request.SupplierOrganizationIds.Count) throw CommerceErrors.Validation("Every invited supplier must be active.");
        if (await db.ProductVariants.CountAsync(x => request.Lines.Select(l => l.VariantId).Contains(x.Id) && x.IsActive, ct) != request.Lines.Count) throw CommerceErrors.Validation("Every RFQ line must reference an active variant.");
        var now = clock.GetUtcNow();
        var rfq = new RequestForQuotation { Id = Guid.CreateVersion7(), Number = $"RFQ-{now:yyyyMMdd}-{RandomNumberGenerator.GetInt32(100000, 999999)}", WarehouseId = request.WarehouseId, Status = RequestForQuotationStatus.Draft, Currency = request.Currency.Trim().ToUpperInvariant(), ResponseDeadline = request.ResponseDeadline, CreatedBy = actor, CreatedAt = now };
        rfq.Lines.AddRange(request.Lines.Select(x => new RequestForQuotationLine { Id = Guid.CreateVersion7(), VariantId = x.VariantId, Quantity = x.Quantity }));
        rfq.Suppliers.AddRange(request.SupplierOrganizationIds.Select(x => new RequestForQuotationSupplier { SupplierOrganizationId = x, InvitedAt = now }));
        db.RequestsForQuotation.Add(rfq); Audit("RFQ_CREATED", "RequestForQuotation", rfq.Id, actor, rfq.Number); await db.SaveChangesAsync(ct);
        return MapRfq(rfq);
    }

    public async Task<RfqDto> OpenRfqAsync(Guid id, string actor, CancellationToken ct)
    {
        await using var transaction = await db.BeginTransactionAsync(ct);
        await db.LockRequestForQuotationAsync(id, ct);
        var rfq = await db.RequestsForQuotation.Include(x => x.Lines).Include(x => x.Suppliers).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw CommerceErrors.NotFound("RFQ");
        if (rfq.Status != RequestForQuotationStatus.Draft || rfq.ResponseDeadline <= clock.GetUtcNow()) throw CommerceErrors.Conflict("rfq_state_invalid", "Only a draft RFQ with a future deadline can be opened.");
        rfq.Status = RequestForQuotationStatus.Open; Audit("RFQ_OPENED", "RequestForQuotation", rfq.Id, actor, rfq.Number); await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); return MapRfq(rfq);
    }

    public async Task<RfqDto> CloseRfqAsync(Guid id, string actor, CancellationToken ct)
    {
        await using var transaction = await db.BeginTransactionAsync(ct);
        await db.LockRequestForQuotationAsync(id, ct);
        var rfq = await db.RequestsForQuotation.Include(x => x.Lines).Include(x => x.Suppliers).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw CommerceErrors.NotFound("RFQ");
        if (rfq.Status != RequestForQuotationStatus.Open) throw CommerceErrors.Conflict("rfq_state_invalid", "Only an open RFQ can be closed.");
        rfq.Status = RequestForQuotationStatus.Closed; Audit("RFQ_CLOSED", "RequestForQuotation", rfq.Id, actor, rfq.Number); await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); return MapRfq(rfq);
    }

    public async Task<IReadOnlyList<RfqDto>> GetRfqsAsync(Guid? supplierId, CancellationToken ct)
    {
        var query = db.RequestsForQuotation.AsNoTracking().Include(x => x.Lines).Include(x => x.Suppliers).AsQueryable();
        if (supplierId is not null) query = query.Where(x => x.Suppliers.Any(s => s.SupplierOrganizationId == supplierId) && x.Status != RequestForQuotationStatus.Draft && x.Status != RequestForQuotationStatus.Cancelled);
        var now = clock.GetUtcNow();
        return (await query.OrderByDescending(x => x.CreatedAt).Take(300).ToListAsync(ct)).Select(x => supplierId is null ? MapRfq(x) : MapSupplierRfq(x, supplierId.Value, now)).ToList();
    }

    public async Task<SupplierQuotationDto> SubmitQuotationAsync(Guid rfqId, SubmitQuotationRequest request, Guid supplierId, string actor, CancellationToken ct)
    {
        if (request.Currency.Trim().Length != 3 || request.Lines.Count is < 1 or > 200 || request.Lines.Select(x => x.RfqLineId).Distinct().Count() != request.Lines.Count || request.Lines.Any(x => string.IsNullOrWhiteSpace(x.SupplierSku) || x.SupplierSku.Length > 80 || !ValidMoney(x.UnitCost) || x.LeadTimeDays < 0 || x.MinimumOrderQuantity < 1 || x.OrderMultiple < 1 || !ValidPercent(x.ReliabilityPercent))) throw CommerceErrors.Validation("Quotation terms are invalid.");
        var requestHash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(request)));
        await using var transaction = await db.BeginTransactionAsync(ct);
        await db.LockRequestForQuotationAsync(rfqId, ct);
        var rfq = await db.RequestsForQuotation.Include(x => x.Lines).Include(x => x.Suppliers).SingleOrDefaultAsync(x => x.Id == rfqId, ct) ?? throw CommerceErrors.NotFound("RFQ");
        if (rfq.Status != RequestForQuotationStatus.Open || rfq.ResponseDeadline <= clock.GetUtcNow()) throw CommerceErrors.Conflict("rfq_state_invalid", "This RFQ is not accepting quotations.");
        if (rfq.Suppliers.All(x => x.SupplierOrganizationId != supplierId)) throw CommerceErrors.NotFound("RFQ");
        if (!string.Equals(rfq.Currency, request.Currency.Trim(), StringComparison.OrdinalIgnoreCase)) throw CommerceErrors.Validation("Quotation currency must match the RFQ.");
        if (request.Lines.Count != rfq.Lines.Count || request.Lines.Any(x => rfq.Lines.All(l => l.Id != x.RfqLineId))) throw CommerceErrors.Validation("A quotation must cover every RFQ line exactly once.");
        var existing = await db.SupplierQuotations.AsNoTracking().Include(x => x.Lines).ThenInclude(x => x.RequestForQuotationLine).SingleOrDefaultAsync(x => x.RequestForQuotationId == rfq.Id && x.SupplierOrganizationId == supplierId, ct);
        if (existing is not null)
        {
            if (existing.RequestHash != requestHash) throw CommerceErrors.Conflict("quotation_already_submitted", "This supplier already submitted different terms for the RFQ.");
            await transaction.CommitAsync(ct); return MapQuotation(existing, true);
        }
        var now = clock.GetUtcNow();
        var quotation = new SupplierQuotation { Id = Guid.CreateVersion7(), Number = $"QT-{now:yyyyMMdd}-{RandomNumberGenerator.GetInt32(100000, 999999)}", RequestForQuotationId = rfq.Id, SupplierOrganizationId = supplierId, Currency = rfq.Currency, Status = SupplierQuotationStatus.Submitted, RequestHash = requestHash, SubmittedBy = actor, SubmittedAt = now };
        quotation.Lines.AddRange(request.Lines.Select(x => new SupplierQuotationLine { Id = Guid.CreateVersion7(), RequestForQuotationId = rfq.Id, RequestForQuotationLineId = x.RfqLineId, RequestForQuotationLine = rfq.Lines.Single(l => l.Id == x.RfqLineId), SupplierSku = x.SupplierSku.Trim(), UnitCost = x.UnitCost, LeadTimeDays = x.LeadTimeDays, MinimumOrderQuantity = x.MinimumOrderQuantity, OrderMultiple = x.OrderMultiple, ReliabilityPercent = x.ReliabilityPercent }));
        db.SupplierQuotations.Add(quotation); Audit("QUOTATION_SUBMITTED", "SupplierQuotation", quotation.Id, actor, quotation.Number); await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); return MapQuotation(quotation);
    }

    public async Task<IReadOnlyList<SupplierQuotationDto>> GetQuotationsAsync(Guid rfqId, CancellationToken ct) =>
        (await db.SupplierQuotations.AsNoTracking().Include(x => x.Lines).ThenInclude(x => x.RequestForQuotationLine).Where(x => x.RequestForQuotationId == rfqId).OrderBy(x => x.SubmittedAt).ToListAsync(ct)).Select(x => MapQuotation(x)).ToList();

    public async Task<AwardQuotationResultDto> AwardQuotationAsync(Guid rfqId, AwardQuotationRequest request, string actor, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 500) throw CommerceErrors.Validation("An award reason is required.");
        await using var transaction = await db.BeginTransactionAsync(ct);
        await db.LockRequestForQuotationAsync(rfqId, ct);
        var rfq = await db.RequestsForQuotation.Include(x => x.Lines).Include(x => x.Suppliers).SingleOrDefaultAsync(x => x.Id == rfqId, ct) ?? throw CommerceErrors.NotFound("RFQ");
        if (rfq.Status is not (RequestForQuotationStatus.Open or RequestForQuotationStatus.Closed)) throw CommerceErrors.Conflict("rfq_state_invalid", "The RFQ is not eligible for award.");
        if (rfq.CreatedBy == actor) throw new CommerceException("maker_checker_required", "The RFQ maker cannot award it.", 403);
        var quotation = await db.SupplierQuotations.Include(x => x.Lines).ThenInclude(x => x.RequestForQuotationLine).SingleOrDefaultAsync(x => x.Id == request.QuotationId && x.RequestForQuotationId == rfq.Id, ct) ?? throw CommerceErrors.NotFound("Quotation");
        if (quotation.Status != SupplierQuotationStatus.Submitted) throw CommerceErrors.Conflict("quotation_state_invalid", "Only a submitted quotation can be awarded.");
        foreach (var line in quotation.Lines)
        {
            var quantity = line.RequestForQuotationLine.Quantity;
            if (quantity < line.MinimumOrderQuantity || quantity % line.OrderMultiple != 0) throw CommerceErrors.Conflict("quotation_terms_incompatible", $"Requested quantity for {line.SupplierSku} does not satisfy the quoted MOQ or order multiple.");
        }
        var now = clock.GetUtcNow();
        var order = new PurchaseOrder { Id = Guid.CreateVersion7(), Number = $"PO-{now:yyyyMMdd}-{RandomNumberGenerator.GetInt32(100000, 999999)}", SupplierOrganizationId = quotation.SupplierOrganizationId, WarehouseId = rfq.WarehouseId, SourceQuotationId = quotation.Id, Status = PurchaseOrderStatus.Draft, Currency = quotation.Currency, ExpectedAt = now.AddDays(quotation.Lines.Max(x => x.LeadTimeDays)), CreatedBy = actor, CreatedAt = now };
        order.Lines.AddRange(quotation.Lines.Select(x => new PurchaseOrderLine { Id = Guid.CreateVersion7(), VariantId = x.RequestForQuotationLine.VariantId, SourceQuotationLineId = x.Id, SupplierSku = x.SupplierSku, OrderedQuantity = x.RequestForQuotationLine.Quantity, UnitCost = x.UnitCost, LeadTimeDays = x.LeadTimeDays, MinimumOrderQuantity = x.MinimumOrderQuantity, OrderMultiple = x.OrderMultiple, ReliabilityPercent = x.ReliabilityPercent }));
        rfq.Status = RequestForQuotationStatus.Awarded; rfq.AwardedQuotationId = quotation.Id; rfq.AwardReason = request.Reason.Trim(); rfq.AwardedBy = actor; rfq.AwardedAt = now; quotation.Status = SupplierQuotationStatus.Awarded;
        var rejected = await db.SupplierQuotations.Where(x => x.RequestForQuotationId == rfq.Id && x.Id != quotation.Id && x.Status == SupplierQuotationStatus.Submitted).ToListAsync(ct);
        foreach (var item in rejected) item.Status = SupplierQuotationStatus.Rejected;
        db.PurchaseOrders.Add(order); Audit("RFQ_AWARDED", "RequestForQuotation", rfq.Id, actor, request.Reason.Trim()); await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        return new(MapRfq(rfq), MapQuotation(quotation), MapOrder(order));
    }

    public async Task<PurchaseOrderDto> CreatePurchaseOrderAsync(CreatePurchaseOrderRequest request, string actor, CancellationToken ct)
    {
        if (request.Lines.Count is < 1 or > 200 || request.Lines.Select(x => x.SupplierSourceId).Distinct().Count() != request.Lines.Count) throw CommerceErrors.Validation("A purchase order requires 1-200 unique source lines.");
        var sources = await db.SupplierProductSources.Where(x => request.Lines.Select(l => l.SupplierSourceId).Contains(x.Id) && x.IsActive).ToDictionaryAsync(x => x.Id, ct);
        if (sources.Count != request.Lines.Count || sources.Values.Any(x => x.SupplierOrganizationId != request.SupplierOrganizationId)) throw CommerceErrors.Validation("Every line must reference an active source owned by the selected supplier.");
        if (!await db.Warehouses.AnyAsync(x => x.Id == request.WarehouseId && x.IsActive, ct)) throw CommerceErrors.NotFound("Warehouse");
        var currencies = sources.Values.Select(x => x.Currency).Distinct().ToArray();
        if (currencies.Length != 1) throw CommerceErrors.Validation("A purchase order cannot mix currencies.");
        foreach (var line in request.Lines)
        {
            var source = sources[line.SupplierSourceId];
            if (line.Quantity < source.MinimumOrderQuantity || line.Quantity % source.OrderMultiple != 0) throw CommerceErrors.Validation($"Quantity for {source.SupplierSku} must meet MOQ {source.MinimumOrderQuantity} and multiple {source.OrderMultiple}.");
        }
        var now = clock.GetUtcNow();
        var order = new PurchaseOrder { Id = Guid.CreateVersion7(), Number = $"PO-{now:yyyyMMdd}-{RandomNumberGenerator.GetInt32(100000, 999999)}", SupplierOrganizationId = request.SupplierOrganizationId, WarehouseId = request.WarehouseId, Status = PurchaseOrderStatus.Draft, Currency = currencies[0], ExpectedAt = request.ExpectedAt, CreatedBy = actor, CreatedAt = now };
        order.Lines.AddRange(request.Lines.Select(x => { var source = sources[x.SupplierSourceId]; return new PurchaseOrderLine { Id = Guid.CreateVersion7(), VariantId = source.VariantId, SupplierSku = source.SupplierSku, OrderedQuantity = x.Quantity, UnitCost = source.UnitCost, LeadTimeDays = source.LeadTimeDays, MinimumOrderQuantity = source.MinimumOrderQuantity, OrderMultiple = source.OrderMultiple, ReliabilityPercent = source.ReliabilityPercent }; }));
        db.PurchaseOrders.Add(order); Audit("PURCHASE_ORDER_CREATED", "PurchaseOrder", order.Id, actor, order.Number); await db.SaveChangesAsync(ct);
        return MapOrder(order);
    }

    public async Task<IReadOnlyList<PurchaseOrderDto>> GetPurchaseOrdersAsync(Guid? supplierId, Guid? scopedSupplierId, CancellationToken ct)
    {
        var query = db.PurchaseOrders.AsNoTracking().Include(x => x.Lines).AsQueryable();
        if (scopedSupplierId is not null) query = query.Where(x => x.SupplierOrganizationId == scopedSupplierId);
        if (supplierId is not null) query = query.Where(x => x.SupplierOrganizationId == supplierId);
        return (await query.OrderByDescending(x => x.CreatedAt).Take(300).ToListAsync(ct)).Select(MapOrder).ToList();
    }

    public async Task<PurchaseOrderDto> ApprovePurchaseOrderAsync(Guid id, string actor, CancellationToken ct)
    {
        await using var transaction = await db.BeginTransactionAsync(ct);
        await db.LockPurchaseOrderAsync(id, ct);
        var order = await db.PurchaseOrders.Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw CommerceErrors.NotFound("Purchase order");
        if (order.Status != PurchaseOrderStatus.Draft) throw CommerceErrors.Conflict("purchase_order_state_invalid", "Only draft purchase orders can be approved.");
        if (order.CreatedBy == actor) throw new CommerceException("maker_checker_required", "The maker cannot approve this purchase order.", 403);
        order.Status = PurchaseOrderStatus.Approved; order.ApprovedBy = actor; order.ApprovedAt = clock.GetUtcNow();
        Audit("PURCHASE_ORDER_APPROVED", "PurchaseOrder", order.Id, actor, order.Number); await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); return MapOrder(order);
    }

    public async Task<InboundShipmentDto> CreateInboundShipmentAsync(CreateInboundShipmentRequest request, Guid supplierId, string idempotencyKey, string actor, CancellationToken ct)
    {
        if (request.Lines.Count is < 1 or > 200 || string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 128) throw CommerceErrors.Validation("An inbound shipment requires lines and a valid idempotency key.");
        var requestHash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(request)));
        await using var transaction = await db.BeginTransactionAsync(ct);
        await db.LockReceiptIdempotencyAsync($"asn:{supplierId:N}:{idempotencyKey}", ct);
        var replay = await db.InboundShipments.AsNoTracking().SingleOrDefaultAsync(x => x.SupplierOrganizationId == supplierId && x.IdempotencyKey == idempotencyKey, ct);
        if (replay is not null)
        {
            if (replay.RequestHash != requestHash) throw CommerceErrors.Conflict("idempotency_key_reused", "The idempotency key was already used for a different inbound shipment.");
            await transaction.CommitAsync(ct);
            return MapShipment(replay, true);
        }
        await db.LockPurchaseOrderAsync(request.PurchaseOrderId, ct);
        var order = await db.PurchaseOrders.Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == request.PurchaseOrderId && x.SupplierOrganizationId == supplierId, ct) ?? throw CommerceErrors.NotFound("Purchase order");
        if (order.Status is not (PurchaseOrderStatus.Approved or PurchaseOrderStatus.Sent or PurchaseOrderStatus.PartiallyReceived)) throw CommerceErrors.Conflict("purchase_order_state_invalid", "The purchase order is not open for shipment.");
        var requested = request.Lines.ToDictionary(x => x.PurchaseOrderLineId);
        if (requested.Count != request.Lines.Count || requested.Values.Any(x => x.ExpectedQuantity < 1) || requested.Keys.Any(x => order.Lines.All(l => l.Id != x))) throw CommerceErrors.Validation("Shipment lines are invalid.");
        var shipped = await db.InboundShipmentLines.Where(x => requested.Keys.Contains(x.PurchaseOrderLineId)).GroupBy(x => x.PurchaseOrderLineId).Select(x => new { Id = x.Key, Quantity = x.Sum(y => y.ExpectedQuantity) }).ToDictionaryAsync(x => x.Id, x => x.Quantity, ct);
        foreach (var line in order.Lines.Where(x => requested.ContainsKey(x.Id))) if (shipped.GetValueOrDefault(line.Id) + requested[line.Id].ExpectedQuantity > line.OrderedQuantity) throw CommerceErrors.Conflict("shipment_exceeds_order", $"Shipment quantity exceeds ordered quantity for {line.SupplierSku}.");
        var now = clock.GetUtcNow();
        var shipment = new InboundShipment { Id = Guid.CreateVersion7(), Number = $"ASN-{now:yyyyMMdd}-{RandomNumberGenerator.GetInt32(100000, 999999)}", SupplierOrganizationId = supplierId, PurchaseOrderId = order.Id, Status = InboundShipmentStatus.Submitted, Carrier = Limited(request.Carrier, 120), TrackingNumber = Limited(request.TrackingNumber, 160), EstimatedArrival = request.EstimatedArrival, CreatedAt = now, IdempotencyKey = idempotencyKey, RequestHash = requestHash };
        shipment.Lines.AddRange(request.Lines.Select(x => new InboundShipmentLine { Id = Guid.CreateVersion7(), PurchaseOrderLineId = x.PurchaseOrderLineId, VariantId = order.Lines.Single(l => l.Id == x.PurchaseOrderLineId).VariantId, ExpectedQuantity = x.ExpectedQuantity }));
        order.Status = PurchaseOrderStatus.Sent; db.InboundShipments.Add(shipment); Audit("INBOUND_SHIPMENT_SUBMITTED", "InboundShipment", shipment.Id, actor, shipment.Number); await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        return MapShipment(shipment);
    }

    public async Task<FiscalInvoiceDto> IngestInvoiceAsync(InvoiceIngestionRequest request, Guid supplierId, string actor, CancellationToken ct)
    {
        if (request.SupplierOrganizationId != supplierId || string.IsNullOrWhiteSpace(request.Provider) || string.IsNullOrWhiteSpace(request.ExternalNumber) || request.Currency.Trim().Length != 3 || !ValidMoney(request.TotalAmount) || request.RawPayload.Length is 0 or > 65536 || request.Lines.Count is < 1 or > 500) throw CommerceErrors.Validation("Invoice payload is invalid.");
        var resolver = invoiceResolvers.SingleOrDefault(x => x.Supports(request.Provider)) ?? throw CommerceErrors.Validation("The invoice provider is not allowlisted.");
        resolver.ValidatePayload(request.RawPayload);
        PurchaseOrder? order = null;
        if (request.PurchaseOrderId is not null) order = await db.PurchaseOrders.Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == request.PurchaseOrderId && x.SupplierOrganizationId == supplierId, ct) ?? throw CommerceErrors.NotFound("Purchase order");
        var payloadHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.RawPayload)));
        var requestHash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(request)));
        var provider = request.Provider.Trim().ToUpperInvariant();
        var externalNumber = request.ExternalNumber.Trim();
        await using var transaction = await db.BeginTransactionAsync(ct);
        await db.LockReceiptIdempotencyAsync($"invoice:{supplierId:N}:{provider}:{externalNumber}", ct);
        var existing = await db.FiscalInvoices.AsNoTracking().SingleOrDefaultAsync(x => x.SupplierOrganizationId == supplierId && x.Provider == provider && x.ExternalNumber == externalNumber, ct);
        if (existing is not null)
        {
            if (existing.RequestHash != requestHash) throw CommerceErrors.Conflict("invoice_number_exists", "This provider invoice number already exists with different content.");
            await transaction.CommitAsync(ct); return MapInvoice(existing);
        }
        var payloadReplay = await db.FiscalInvoices.AsNoTracking().SingleOrDefaultAsync(x => x.SupplierOrganizationId == supplierId && x.PayloadHash == payloadHash, ct);
        if (payloadReplay is not null) throw CommerceErrors.Conflict("invoice_payload_exists", "This invoice payload was already ingested under another invoice number.");
        var orderLineIds = order?.Lines.Select(x => x.Id).ToHashSet() ?? [];
        if (request.Lines.Any(x => x.PurchaseOrderLineId is not null && !orderLineIds.Contains(x.PurchaseOrderLineId.Value))) throw CommerceErrors.Validation("Invoice lines may reference only lines from the linked purchase order.");
        if (request.Lines.Where(x => x.PurchaseOrderLineId is not null).GroupBy(x => x.PurchaseOrderLineId).Any(x => x.Count() > 1)) throw CommerceErrors.Validation("An invoice may contain only one line per linked purchase-order line.");
        var invoice = new FiscalInvoice { Id = Guid.CreateVersion7(), SupplierOrganizationId = supplierId, PurchaseOrderId = request.PurchaseOrderId, Provider = provider, ExternalNumber = externalNumber, Currency = request.Currency.Trim().ToUpperInvariant(), TotalAmount = request.TotalAmount, Status = FiscalInvoiceStatus.Ingested, PayloadHash = payloadHash, RequestHash = requestHash, DocumentReference = Limited(request.DocumentReference, 1000), IssuedAt = request.IssuedAt, IngestedAt = clock.GetUtcNow() };
        invoice.Lines.AddRange(request.Lines.Select(x => new FiscalInvoiceLine { Id = Guid.CreateVersion7(), PurchaseOrderLineId = x.PurchaseOrderLineId, VariantId = order?.Lines.SingleOrDefault(l => l.Id == x.PurchaseOrderLineId)?.VariantId, SupplierSku = x.SupplierSku.Trim(), Quantity = x.Quantity, UnitCost = x.UnitCost }));
        if (invoice.Lines.Any(x => x.Quantity < 1 || !ValidMoney(x.UnitCost))) throw CommerceErrors.Validation("Invoice lines are invalid.");
        db.FiscalInvoices.Add(invoice); Audit("FISCAL_INVOICE_INGESTED", "FiscalInvoice", invoice.Id, actor, $"{invoice.Provider}:{invoice.ExternalNumber}"); await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        return MapInvoice(invoice);
    }

    public async Task<GoodsReceiptDto> PostReceiptAsync(PostGoodsReceiptRequest request, string idempotencyKey, string actor, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 128 || request.Lines.Count is < 1 or > 200) throw CommerceErrors.Validation("A valid idempotency key and receipt lines are required.");
        var requestHash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(request)));
        await using var transaction = await db.BeginTransactionAsync(ct);
        await db.LockReceiptIdempotencyAsync(idempotencyKey, ct);
        var replay = await db.GoodsReceipts.AsNoTracking().Include(x => x.Lines).SingleOrDefaultAsync(x => x.IdempotencyKey == idempotencyKey, ct);
        if (replay is not null)
        {
            if (replay.RequestHash != requestHash) throw CommerceErrors.Conflict("idempotency_key_reused", "The idempotency key was already used for a different receipt.");
            await transaction.CommitAsync(ct);
            return MapReceipt(replay, true);
        }
        ValidateReceiptLines(request.Lines);
        await db.LockPurchaseOrderAsync(request.PurchaseOrderId, ct);
        var order = await db.PurchaseOrders.Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == request.PurchaseOrderId && x.WarehouseId == request.WarehouseId, ct) ?? throw CommerceErrors.NotFound("Purchase order");
        if (order.Status is not (PurchaseOrderStatus.Approved or PurchaseOrderStatus.Sent or PurchaseOrderStatus.PartiallyReceived)) throw CommerceErrors.Conflict("purchase_order_state_invalid", "The purchase order is not open for receiving.");
        InboundShipment? inbound = null;
        if (request.InboundShipmentId is not null) inbound = await db.InboundShipments.Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == request.InboundShipmentId && x.PurchaseOrderId == order.Id, ct) ?? throw CommerceErrors.NotFound("Inbound shipment");
        var requestedIds = request.Lines.Select(x => x.PurchaseOrderLineId).ToArray();
        if (requestedIds.Distinct().Count() != requestedIds.Length || requestedIds.Any(x => order.Lines.All(l => l.Id != x))) throw CommerceErrors.Validation("Receipt lines must uniquely reference this purchase order.");
        if (inbound is not null)
        {
            var prior = await db.GoodsReceiptLines.Where(x => x.GoodsReceipt.InboundShipmentId == inbound.Id && requestedIds.Contains(x.PurchaseOrderLineId)).GroupBy(x => x.PurchaseOrderLineId).Select(x => new { Id = x.Key, Quantity = x.Sum(y => y.PhysicalQuantity) }).ToDictionaryAsync(x => x.Id, x => x.Quantity, ct);
            foreach (var input in request.Lines)
            {
                var shipmentLine = inbound.Lines.SingleOrDefault(x => x.PurchaseOrderLineId == input.PurchaseOrderLineId) ?? throw CommerceErrors.Validation("Receipt lines must belong to the referenced inbound shipment.");
                if (prior.GetValueOrDefault(input.PurchaseOrderLineId) + input.PhysicalQuantity > shipmentLine.ExpectedQuantity) throw CommerceErrors.Conflict("receipt_exceeds_shipment", "Receipt quantity exceeds the referenced inbound shipment.");
            }
        }
        var variantIds = order.Lines.Where(x => requestedIds.Contains(x.Id)).Select(x => x.VariantId).Distinct().Order().ToArray();
        await db.LockInventoryAsync(variantIds, ct);
        var inventories = await db.Inventory.Include(x => x.Variant).ThenInclude(x => x.Product).Where(x => variantIds.Contains(x.VariantId)).ToDictionaryAsync(x => x.VariantId, ct);
        var stocks = await db.WarehouseStocks.Where(x => x.WarehouseId == request.WarehouseId && variantIds.Contains(x.VariantId)).ToDictionaryAsync(x => x.VariantId, ct);
        var receipt = new GoodsReceipt { Id = Guid.CreateVersion7(), Number = $"GRN-{clock.GetUtcNow():yyyyMMdd}-{RandomNumberGenerator.GetInt32(100000, 999999)}", PurchaseOrderId = order.Id, InboundShipmentId = request.InboundShipmentId, WarehouseId = request.WarehouseId, IdempotencyKey = idempotencyKey, RequestHash = requestHash, Status = GoodsReceiptStatus.Posted, ReceivedBy = actor, ReceivedAt = clock.GetUtcNow() };
        foreach (var input in request.Lines)
        {
            var orderLine = order.Lines.Single(x => x.Id == input.PurchaseOrderLineId);
            if (orderLine.ReceivedQuantity + input.PhysicalQuantity > orderLine.OrderedQuantity) throw CommerceErrors.Conflict("receipt_exceeds_order", $"Receipt exceeds ordered quantity for {orderLine.SupplierSku}.");
            InventoryLot? lot = null;
            if (!string.IsNullOrWhiteSpace(input.LotNumber))
            {
                var lotNumber = input.LotNumber.Trim();
                lot = await db.InventoryLots.SingleOrDefaultAsync(x => x.VariantId == orderLine.VariantId && x.LotNumber == lotNumber, ct);
                if (lot is null) { lot = new InventoryLot { Id = Guid.CreateVersion7(), VariantId = orderLine.VariantId, LotNumber = lotNumber, ExpiresAt = input.ExpiresAt, CreatedAt = clock.GetUtcNow() }; db.InventoryLots.Add(lot); }
            }
            var line = new GoodsReceiptLine { Id = Guid.CreateVersion7(), PurchaseOrderLineId = orderLine.Id, VariantId = orderLine.VariantId, LotId = lot?.Id, ExpectedQuantity = input.ExpectedQuantity, PhysicalQuantity = input.PhysicalQuantity, AcceptedQuantity = input.AcceptedQuantity, DamagedQuantity = input.DamagedQuantity, QuarantinedQuantity = input.QuarantinedQuantity, MissingQuantity = Math.Max(0, input.ExpectedQuantity - input.PhysicalQuantity) };
            receipt.Lines.Add(line); orderLine.ReceivedQuantity += input.PhysicalQuantity;
            await AddBalanceAsync(request.WarehouseId, orderLine.VariantId, lot?.Id, InventoryState.Available, input.AcceptedQuantity, ct);
            await AddBalanceAsync(request.WarehouseId, orderLine.VariantId, lot?.Id, InventoryState.Damaged, input.DamagedQuantity, ct);
            await AddBalanceAsync(request.WarehouseId, orderLine.VariantId, lot?.Id, InventoryState.Quarantined, input.QuarantinedQuantity, ct);
            if (!inventories.TryGetValue(orderLine.VariantId, out var inventory)) throw CommerceErrors.NotFound("Inventory");
            inventory.QuantityOnHand += input.AcceptedQuantity; inventory.UpdatedAt = clock.GetUtcNow();
            if (!stocks.TryGetValue(orderLine.VariantId, out var stock)) { stock = new WarehouseStock { WarehouseId = request.WarehouseId, VariantId = orderLine.VariantId, UpdatedAt = clock.GetUtcNow() }; db.WarehouseStocks.Add(stock); stocks[orderLine.VariantId] = stock; }
            stock.OnHand += input.AcceptedQuantity; stock.UpdatedAt = clock.GetUtcNow();
            AddReceiptLedger(receipt, line, InventoryState.Available, input.AcceptedQuantity, actor);
            AddReceiptLedger(receipt, line, InventoryState.Damaged, input.DamagedQuantity, actor);
            AddReceiptLedger(receipt, line, InventoryState.Quarantined, input.QuarantinedQuantity, actor);
        }
        order.Status = order.Lines.All(x => x.ReceivedQuantity == x.OrderedQuantity) ? PurchaseOrderStatus.Received : PurchaseOrderStatus.PartiallyReceived;
        db.GoodsReceipts.Add(receipt); Audit("GOODS_RECEIPT_POSTED", "GoodsReceipt", receipt.Id, actor, receipt.Number); await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        return MapReceipt(receipt, false);
    }

    public async Task<IReadOnlyList<InventoryBalanceDto>> GetBalancesAsync(Guid? warehouseId, Guid? variantId, CancellationToken ct)
    {
        var query = db.InventoryBalances.AsNoTracking().AsQueryable();
        if (warehouseId is not null) query = query.Where(x => x.WarehouseId == warehouseId);
        if (variantId is not null) query = query.Where(x => x.VariantId == variantId);
        return await query.OrderBy(x => x.WarehouseId).ThenBy(x => x.VariantId).ThenBy(x => x.State).Select(x => new InventoryBalanceDto(x.WarehouseId, x.LocationId, x.VariantId, x.LotId, x.State.ToString(), x.Quantity, x.UpdatedAt)).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<InboundShipmentDto>> GetInboundShipmentsAsync(Guid? supplierId, CancellationToken ct)
    {
        var query = db.InboundShipments.AsNoTracking().AsQueryable();
        if (supplierId is not null) query = query.Where(x => x.SupplierOrganizationId == supplierId);
        return await query.OrderByDescending(x => x.CreatedAt).Take(300).Select(x => new InboundShipmentDto(x.Id, x.Number, x.SupplierOrganizationId, x.PurchaseOrderId, x.Status.ToString(), x.Carrier, x.TrackingNumber, x.EstimatedArrival, x.CreatedAt)).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<FiscalInvoiceDto>> GetInvoicesAsync(Guid? supplierId, CancellationToken ct)
    {
        var query = db.FiscalInvoices.AsNoTracking().AsQueryable();
        if (supplierId is not null) query = query.Where(x => x.SupplierOrganizationId == supplierId);
        return await query.OrderByDescending(x => x.IngestedAt).Take(300).Select(x => new FiscalInvoiceDto(x.Id, x.SupplierOrganizationId, x.PurchaseOrderId, x.Provider, x.ExternalNumber, new MoneyDto(x.TotalAmount, x.Currency), x.Status.ToString(), x.IssuedAt, x.IngestedAt)).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<GoodsReceiptDto>> GetReceiptsAsync(Guid? warehouseId, CancellationToken ct)
    {
        var query = db.GoodsReceipts.AsNoTracking().Include(x => x.Lines).AsQueryable();
        if (warehouseId is not null) query = query.Where(x => x.WarehouseId == warehouseId);
        return (await query.OrderByDescending(x => x.ReceivedAt).Take(300).ToListAsync(ct)).Select(x => MapReceipt(x, false)).ToList();
    }

    public async Task<InvoiceMatchDto> MatchInvoiceAsync(Guid invoiceId, string actor, CancellationToken ct)
    {
        await using var transaction = await db.BeginTransactionAsync(ct);
        await db.LockReceiptIdempotencyAsync($"invoice-match:{invoiceId:N}", ct);
        var invoice = await db.FiscalInvoices.Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == invoiceId, ct) ?? throw CommerceErrors.NotFound("Fiscal invoice");
        if (invoice.PurchaseOrderId is null) throw CommerceErrors.Conflict("invoice_not_linked", "The invoice is not linked to a purchase order.");
        await db.LockPurchaseOrderAsync(invoice.PurchaseOrderId.Value, ct);
        var order = await db.PurchaseOrders.Include(x => x.Lines).SingleAsync(x => x.Id == invoice.PurchaseOrderId, ct);
        var exceptions = new List<string>();
        if (invoice.Currency != order.Currency) exceptions.Add("Currency differs from purchase order.");
        var lineTotal = invoice.Lines.Sum(x => x.Quantity * x.UnitCost);
        if (lineTotal != invoice.TotalAmount) exceptions.Add("Invoice total differs from the sum of invoice lines.");
        var linkedLineIds = invoice.Lines.Where(x => x.PurchaseOrderLineId is not null).Select(x => x.PurchaseOrderLineId!.Value).Distinct().ToArray();
        var previouslyMatched = await db.FiscalInvoiceLines.AsNoTracking()
            .Where(x => x.FiscalInvoiceId != invoice.Id && x.FiscalInvoice.Status == FiscalInvoiceStatus.Matched && x.PurchaseOrderLineId != null && linkedLineIds.Contains(x.PurchaseOrderLineId.Value))
            .GroupBy(x => x.PurchaseOrderLineId!.Value).Select(x => new { Id = x.Key, Quantity = x.Sum(y => y.Quantity) }).ToDictionaryAsync(x => x.Id, x => x.Quantity, ct);
        foreach (var lineGroup in invoice.Lines.GroupBy(x => x.PurchaseOrderLineId))
        {
            var first = lineGroup.First();
            var orderLine = lineGroup.Key is null ? null : order.Lines.SingleOrDefault(x => x.Id == lineGroup.Key);
            if (orderLine is null) { exceptions.Add($"Invoice line {first.SupplierSku} is not linked to the purchase order."); continue; }
            var quantity = lineGroup.Sum(x => x.Quantity);
            if (previouslyMatched.GetValueOrDefault(orderLine.Id) + quantity > orderLine.ReceivedQuantity) exceptions.Add($"{first.SupplierSku}: cumulative invoiced quantity exceeds physically received quantity.");
            if (lineGroup.Any(x => x.UnitCost != orderLine.UnitCost)) exceptions.Add($"{first.SupplierSku}: invoice unit cost differs from purchase order.");
        }
        var match = await db.InvoiceMatches.SingleOrDefaultAsync(x => x.FiscalInvoiceId == invoice.Id, ct);
        if (match is null) { match = new InvoiceMatch { Id = Guid.CreateVersion7(), PurchaseOrderId = order.Id, FiscalInvoiceId = invoice.Id }; db.InvoiceMatches.Add(match); }
        match.Status = exceptions.Count == 0 ? MatchStatus.Matched : MatchStatus.Exception; match.ExceptionsJson = JsonSerializer.Serialize(exceptions); match.EvaluatedAt = clock.GetUtcNow(); invoice.Status = exceptions.Count == 0 ? FiscalInvoiceStatus.Matched : FiscalInvoiceStatus.Exception;
        Audit("INVOICE_MATCH_EVALUATED", "FiscalInvoice", invoice.Id, actor, match.Status.ToString()); await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        return new(match.Id, order.Id, invoice.Id, match.Status.ToString(), exceptions, match.EvaluatedAt);
    }

    public async Task<Guid> GetSupplierScopeAsync(string actor, CancellationToken ct)
    {
        if (!Guid.TryParseExact(actor, "N", out var applicationUserId)) throw new CommerceException("invalid_identity", "The supplier identity is invalid.", 401);
        var supplierIds = await db.SupplierUsers.AsNoTracking().Where(x => x.ApplicationUserId == applicationUserId && x.IsActive).Select(x => x.SupplierOrganizationId).Take(2).ToListAsync(ct);
        return supplierIds.Count switch { 1 => supplierIds[0], 0 => throw new CommerceException("supplier_scope_required", "The user is not assigned to a supplier organization.", 403), _ => throw new CommerceException("supplier_scope_ambiguous", "The user has multiple supplier organizations; explicit organization selection is not supported.", 403) };
    }

    private async Task AddBalanceAsync(Guid warehouseId, Guid variantId, Guid? lotId, InventoryState state, int quantity, CancellationToken ct)
    {
        if (quantity == 0) return;
        var balance = await db.InventoryBalances.SingleOrDefaultAsync(x => x.WarehouseId == warehouseId && x.LocationId == null && x.VariantId == variantId && x.LotId == lotId && x.State == state, ct);
        if (balance is null) db.InventoryBalances.Add(new InventoryBalance { Id = Guid.CreateVersion7(), WarehouseId = warehouseId, VariantId = variantId, LotId = lotId, State = state, Quantity = quantity, UpdatedAt = clock.GetUtcNow() }); else { balance.Quantity += quantity; balance.UpdatedAt = clock.GetUtcNow(); }
    }

    private void AddReceiptLedger(GoodsReceipt receipt, GoodsReceiptLine line, InventoryState state, int quantity, string actor)
    {
        if (quantity == 0) return;
        db.InventoryLedgerEntries.Add(new InventoryLedgerEntry { Id = Guid.CreateVersion7(), VariantId = line.VariantId, WarehouseId = receipt.WarehouseId, LotId = line.LotId, QuantityDelta = quantity, State = state, Reason = state == InventoryState.Damaged ? InventoryMovementReason.Damage : InventoryMovementReason.GoodsReceived, ReferenceType = "GoodsReceipt", ReferenceId = receipt.Id.ToString(), CreatedBy = actor, CreatedAt = receipt.ReceivedAt });
    }

    private static void ValidateReceiptLines(IReadOnlyList<ReceiptLineRequest> lines)
    {
        foreach (var line in lines)
            if (line.ExpectedQuantity < 1 || line.PhysicalQuantity < 0 || line.AcceptedQuantity < 0 || line.DamagedQuantity < 0 || line.QuarantinedQuantity < 0 || line.PhysicalQuantity != line.AcceptedQuantity + line.DamagedQuantity + line.QuarantinedQuantity)
                throw CommerceErrors.Validation("Each physical quantity must equal accepted plus damaged plus quarantined quantity.");
    }

    private void Audit(string eventType, string resourceType, Guid resourceId, string actor, string reason) => db.OperationsAuditEntries.Add(new OperationsAuditEntry { Id = Guid.CreateVersion7(), EventType = eventType, ResourceType = resourceType, ResourceId = resourceId.ToString(), ActorId = actor, Reason = reason[..Math.Min(reason.Length, 500)], CreatedAt = clock.GetUtcNow() });
    private static string? Limited(string? value, int max) => string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, max)];
    private static SupplierDto MapSupplier(SupplierOrganization x) => new(x.Id, x.Code, x.LegalName, x.CountryCode, x.TaxIdentifier, x.Status.ToString(), x.CreatedAt);
    private static SupplierSourceDto MapSource(SupplierProductSource x) => new(x.Id, x.SupplierOrganizationId, x.VariantId, x.SupplierSku, new(x.UnitCost, x.Currency), x.LeadTimeDays, x.MinimumOrderQuantity, x.OrderMultiple, x.ReliabilityPercent, x.IsActive);
    private static RfqDto MapRfq(RequestForQuotation x) => new(x.Id, x.Number, x.WarehouseId, x.Status.ToString(), x.Currency, x.ResponseDeadline, x.CreatedBy, x.CreatedAt, x.AwardedQuotationId, x.Suppliers.Select(s => s.SupplierOrganizationId).ToList(), x.Lines.Select(l => new RfqLineDto(l.Id, l.VariantId, l.Quantity)).ToList());
    private static RfqDto MapSupplierRfq(RequestForQuotation x, Guid supplierId, DateTimeOffset now) => new(x.Id, x.Number, x.WarehouseId, x.Status == RequestForQuotationStatus.Open && x.ResponseDeadline <= now ? RequestForQuotationStatus.Closed.ToString() : x.Status.ToString(), x.Currency, x.ResponseDeadline, "", x.CreatedAt, null, [supplierId], x.Lines.Select(l => new RfqLineDto(l.Id, l.VariantId, l.Quantity)).ToList());
    private static SupplierQuotationDto MapQuotation(SupplierQuotation x, bool replayed = false)
    {
        var lines = x.Lines.Select(l => new QuotationLineDto(l.RequestForQuotationLineId, l.RequestForQuotationLine.VariantId, l.SupplierSku, l.RequestForQuotationLine.Quantity, new(l.UnitCost, x.Currency), l.LeadTimeDays, l.MinimumOrderQuantity, l.OrderMultiple, l.ReliabilityPercent, l.UnitCost * l.RequestForQuotationLine.Quantity)).ToList();
        return new(x.Id, x.Number, x.RequestForQuotationId, x.SupplierOrganizationId, x.Status.ToString(), new(lines.Sum(l => l.ExtendedCost), x.Currency), x.SubmittedAt, lines, replayed);
    }
    private static PurchaseOrderDto MapOrder(PurchaseOrder x) => new(x.Id, x.Number, x.SupplierOrganizationId, x.WarehouseId, x.SourceQuotationId, x.Revision, x.Status.ToString(), x.Currency, x.ExpectedAt, x.CreatedAt, x.CreatedBy, x.ApprovedBy, x.Lines.Select(l => new PurchaseOrderLineDto(l.Id, l.VariantId, l.SourceQuotationLineId, l.SupplierSku, l.OrderedQuantity, l.ReceivedQuantity, new(l.UnitCost, x.Currency), l.LeadTimeDays, l.MinimumOrderQuantity, l.OrderMultiple, l.ReliabilityPercent)).ToList());
    private static InboundShipmentDto MapShipment(InboundShipment x, bool replayed = false) => new(x.Id, x.Number, x.SupplierOrganizationId, x.PurchaseOrderId, x.Status.ToString(), x.Carrier, x.TrackingNumber, x.EstimatedArrival, x.CreatedAt, replayed);
    private static FiscalInvoiceDto MapInvoice(FiscalInvoice x) => new(x.Id, x.SupplierOrganizationId, x.PurchaseOrderId, x.Provider, x.ExternalNumber, new(x.TotalAmount, x.Currency), x.Status.ToString(), x.IssuedAt, x.IngestedAt);
    private static GoodsReceiptDto MapReceipt(GoodsReceipt x, bool replayed) => new(x.Id, x.Number, x.PurchaseOrderId, x.InboundShipmentId, x.WarehouseId, x.Status.ToString(), x.ReceivedBy, x.ReceivedAt, x.Lines.Select(l => new GoodsReceiptLineDto(l.PurchaseOrderLineId, l.VariantId, l.ExpectedQuantity, l.PhysicalQuantity, l.AcceptedQuantity, l.DamagedQuantity, l.QuarantinedQuantity, l.MissingQuantity, l.LotId)).ToList(), replayed);
    private static bool ValidMoney(decimal value) => value >= 0 && value <= 999_999_999_999_999.9999m && decimal.Round(value, 4) == value;
    private static bool ValidPercent(decimal? value) => value is null || value is >= 0 and <= 100 && decimal.Round(value.Value, 3) == value.Value;
}
