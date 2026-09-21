using System.Text.Json;
using Commerce.Contracts;
using Commerce.Domain;
using Microsoft.EntityFrameworkCore;

namespace Commerce.Application;

public sealed class OperationsService(ICommerceDbContext db, IReadModelCache cache, TimeProvider clock)
{
    private const string ForecastVersion = "moving-average-v1";

    public async Task<OperationsDashboardDto> GetDashboardAsync(CancellationToken cancellationToken)
    {
        await ApplyDuePricesAsync("system", cancellationToken);
        var now = clock.GetUtcNow();
        var activeProducts = await db.Products.CountAsync(x => x.Status == ProductStatus.Active, cancellationToken);
        var activeVariants = await db.ProductVariants.CountAsync(x => x.IsActive, cancellationToken);
        var scheduledPrices = await db.PriceRecords.CountAsync(x => x.Status == OperationalStatus.Scheduled, cancellationToken);
        var pendingRecommendations = await db.PriceRecommendations.CountAsync(x => x.Status == OperationalStatus.Proposed || x.Status == OperationalStatus.ReviewRequired, cancellationToken);
        var activePromotions = await db.Promotions.CountAsync(x => x.Status == OperationalStatus.Applied && x.StartsAt <= now && x.EndsAt > now, cancellationToken);
        var totalOnHand = await db.Inventory.SumAsync(x => x.QuantityOnHand, cancellationToken);
        var outOfStock = await db.Inventory.CountAsync(x => x.QuantityOnHand == 0, cancellationToken);
        var lowStock = await db.Inventory.CountAsync(x => x.QuantityOnHand > 0 && x.QuantityOnHand <= 10, cancellationToken);
        var recentUnits = await db.Orders.Where(x => x.CreatedAt >= now.AddDays(-30)).SelectMany(x => x.Items).SumAsync(x => (int?)x.Quantity, cancellationToken) ?? 0;
        var forecastUnits = await db.DemandForecasts.Where(x => x.GeneratedAt >= now.AddDays(-30)).OrderByDescending(x => x.GeneratedAt).SumAsync(x => (decimal?)x.PredictedUnits, cancellationToken);
        var alertRows = await db.OperationalAlerts.AsNoTracking().Where(x => x.Status == AlertStatus.Open).OrderByDescending(x => x.UpdatedAt).Take(8).ToListAsync(cancellationToken);
        var auditRows = await db.OperationsAuditEntries.AsNoTracking().OrderByDescending(x => x.CreatedAt).Take(8).ToListAsync(cancellationToken);
        var alerts = alertRows.Select(MapAlert).ToList();
        var audit = auditRows.Select(MapAudit).ToList();
        return new OperationsDashboardDto(
            [new("Active products", activeProducts, "count"), new("Active variants", activeVariants, "count"), new("Unpublished changes", null, "count", "Product-profile workflow is not implemented."), new("Missing critical data", null, "count", "Critical-data rules are not configured.")],
            [new("Active promotions", activePromotions, "count"), new("Scheduled price changes", scheduledPrices, "count"), new("Recommendations awaiting review", pendingRecommendations, "count"), new("Unusual price movements", await db.OperationalAlerts.CountAsync(x => x.Status == AlertStatus.Open && x.Type == "PRICE_DROP_TOO_LARGE", cancellationToken), "count")],
            [new("Low-stock SKUs", lowStock, "count"), new("Out-of-stock SKUs", outOfStock, "count"), new("Total units on hand", totalOnHand, "units"), new("Inventory value", null, "currency", "Unavailable where unit cost is missing.")],
            [new("Units sold, 30 days", recentUnits, "units"), new("Current forecast", forecastUnits, "units", forecastUnits is null ? "No generated forecasts." : null)],
            alerts, audit, now);
    }

    public async Task<AdminVariantPageDto> GetVariantsAsync(string? query, int page, int pageSize, CancellationToken cancellationToken)
    {
        if (page < 1 || pageSize is < 1 or > 100) throw CommerceErrors.Validation("page and pageSize are out of range.");
        var variants = db.ProductVariants.AsNoTracking().Include(x => x.Product).ThenInclude(x => x.Category).Include(x => x.Inventory).AsQueryable();
        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim().ToLower();
            variants = variants.Where(x => x.Sku.ToLower().Contains(term) || x.Product.Title.ToLower().Contains(term));
        }
        var total = await variants.CountAsync(cancellationToken);
        var rows = await variants.OrderBy(x => x.Product.Title).ThenBy(x => x.Sku).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new { x.ProductId, VariantId = x.Id, x.Sku, Product = x.Product.Title, Variant = x.Name, Category = x.Product.Category.Name, Status = x.IsActive ? "Active" : "Inactive", x.Price, x.Currency, x.UnitCost, x.Inventory.QuantityOnHand }).ToListAsync(cancellationToken);
        var demand = await AverageDailyDemandAsync(rows.Select(x => x.VariantId).ToArray(), 30, cancellationToken);
        return new AdminVariantPageDto(rows.Select(x =>
        {
            var avg = demand.GetValueOrDefault(x.VariantId);
            var health = ClassifyInventory(x.QuantityOnHand, 0, 0, avg, 7);
            return new AdminVariantRowDto(x.ProductId, x.VariantId, x.Sku, x.Product, x.Variant, x.Category, x.Status, new(x.Price, x.Currency), x.UnitCost is null ? null : new(x.UnitCost.Value, x.Currency), x.QuantityOnHand, x.QuantityOnHand, health.ToString(), OperationsCalculations.DaysOfSupply(x.QuantityOnHand, avg));
        }).ToList(), page, pageSize, total, (int)Math.Ceiling((double)total / pageSize));
    }

    public async Task<PricingCurveDto> GetPricingCurveAsync(Guid variantId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        if (from >= to || to - from > TimeSpan.FromDays(366)) throw CommerceErrors.Validation("The pricing window must be positive and no longer than one year.");
        var variant = await db.ProductVariants.AsNoTracking().Include(x => x.Product).SingleOrDefaultAsync(x => x.Id == variantId, cancellationToken) ?? throw CommerceErrors.NotFound("Variant");
        var prices = await db.PriceRecords.AsNoTracking().Where(x => x.VariantId == variantId && x.EffectiveFrom >= from && x.EffectiveFrom <= to).OrderBy(x => x.EffectiveFrom).ToListAsync(cancellationToken);
        var observations = await db.DemandObservations.AsNoTracking().Where(x => x.VariantId == variantId && x.PeriodStart >= from && x.PeriodStart <= to).OrderBy(x => x.PeriodStart).ToListAsync(cancellationToken);
        var forecasts = await db.DemandForecasts.AsNoTracking().Where(x => x.VariantId == variantId && x.GeneratedAt >= from && x.GeneratedAt <= to).OrderBy(x => x.GeneratedAt).ToListAsync(cancellationToken);
        var points = prices.Select(x => new PricePointDto(x.EffectiveFrom, x.Price, null, null, OperationsCalculations.GrossMarginPercent(x.Price, x.CostAtTime), null, null, "Observed price"))
            .Concat(observations.Select(x => new PricePointDto(x.PeriodStart, x.EffectivePrice, x.UnitsOrdered, x.Revenue, OperationsCalculations.GrossMarginPercent(x.EffectivePrice, variant.UnitCost), null, null, "Observed demand")))
            .Concat(forecasts.Select(x => new PricePointDto(x.GeneratedAt, null, null, null, null, null, x.PredictedUnits, "Forecast"))).OrderBy(x => x.Timestamp).ToList();
        return new PricingCurveDto(variant.Id, variant.Sku, variant.Product.Title, new(variant.Price, variant.Currency), variant.UnitCost is null ? null : new(variant.UnitCost.Value, variant.Currency), points, "Observed demand uses stored aggregate periods. Forecast points are model-derived and do not imply causality.");
    }

    public async Task<PriceRecordDto> SchedulePriceAsync(PriceScheduleRequest request, string actor, CancellationToken cancellationToken)
    {
        var variant = await db.ProductVariants.Include(x => x.Product).SingleOrDefaultAsync(x => x.Id == request.VariantId, cancellationToken) ?? throw CommerceErrors.NotFound("Variant");
        ValidatePriceRequest(request);
        if (!Enum.TryParse<PriceKind>(request.Kind, true, out var kind)) throw CommerceErrors.Validation("Kind must be Regular, Promotion, or Markdown.");
        var overlaps = await OverlappingPricesAsync(request.VariantId, kind, request.EffectiveFrom, request.EffectiveUntil, null, cancellationToken);
        var superseded = kind == PriceKind.Regular ? overlaps.Where(x => x.Status == OperationalStatus.Applied && x.EffectiveFrom <= request.EffectiveFrom).ToList() : [];
        if (overlaps.Except(superseded).Any()) throw CommerceErrors.Conflict("price_interval_overlap", "An incompatible price interval already exists for this variant and price kind.");
        var policy = await PolicyAsync(variant.Product.CategoryId, variant.Currency, cancellationToken);
        var errors = GuardrailErrors(variant.Price, request.Price, variant.UnitCost, policy).ToArray();
        if (errors.Length > 0) throw CommerceErrors.Conflict("pricing_guardrail_violation", string.Join(" ", errors));
        var changePercent = variant.Price == 0 ? 100m : Math.Abs((request.Price - variant.Price) / variant.Price * 100m);
        var requiresApproval = changePercent >= policy.ApprovalThresholdPercent;
        var now = clock.GetUtcNow();
        var record = new PriceRecord { Id = Guid.CreateVersion7(), VariantId = variant.Id, Currency = variant.Currency, Price = request.Price, CompareAtPrice = request.CompareAtPrice, CostAtTime = variant.UnitCost, EffectiveFrom = request.EffectiveFrom, EffectiveUntil = request.EffectiveUntil, Reason = request.Reason.Trim(), CreatedBy = actor, CreatedAt = now, Source = "Manual", Revision = await NextRevisionAsync(variant.Id, cancellationToken), Kind = kind, Status = requiresApproval ? OperationalStatus.ReviewRequired : request.EffectiveFrom > now ? OperationalStatus.Scheduled : OperationalStatus.Applied, ApprovedBy = requiresApproval ? null : actor, ApprovedAt = requiresApproval ? null : now };
        db.PriceRecords.Add(record);
        if (record.Status != OperationalStatus.ReviewRequired) foreach (var prior in superseded) prior.EffectiveUntil = record.EffectiveFrom;
        if (record.Status == OperationalStatus.Applied) await MaterializeEffectivePriceAsync(variant, now, [record], cancellationToken);
        Audit("PRICE_SCHEDULED", "PriceRecord", record.Id.ToString(), actor, null, new { record.Id, record.VariantId, record.Currency, record.Price, record.CompareAtPrice, record.CostAtTime, record.EffectiveFrom, record.EffectiveUntil, record.Kind, record.Status, record.Source, record.Revision }, request.Reason);
        await db.SaveChangesAsync(cancellationToken);
        if (record.Status == OperationalStatus.Applied) await InvalidatePricingAsync(variant.Id, variant.Product.Slug);
        return MapPrice(record);
    }

    public async Task<PriceRecordDto> ApprovePriceAsync(Guid id, string actor, string reason, CancellationToken cancellationToken)
    {
        var record = await db.PriceRecords.Include(x => x.Variant).ThenInclude(x => x.Product).SingleOrDefaultAsync(x => x.Id == id, cancellationToken) ?? throw CommerceErrors.NotFound("Price record");
        if (record.Status != OperationalStatus.ReviewRequired) throw CommerceErrors.Conflict("approval_state_invalid", "Only review-required price changes can be approved.");
        if (string.Equals(record.CreatedBy, actor, StringComparison.Ordinal)) throw new CommerceException("maker_checker_required", "The maker cannot approve this price change.", 403);
        var before = new { record.Status, record.ApprovedBy };
        var now = clock.GetUtcNow();
        var overlaps = await OverlappingPricesAsync(record.VariantId, record.Kind, record.EffectiveFrom, record.EffectiveUntil, record.Id, cancellationToken);
        var superseded = record.Kind == PriceKind.Regular ? overlaps.Where(x => x.Status == OperationalStatus.Applied && x.EffectiveFrom <= record.EffectiveFrom).ToList() : [];
        if (overlaps.Except(superseded).Any()) throw CommerceErrors.Conflict("price_interval_overlap", "An incompatible price interval was created while this change awaited approval.");
        record.ApprovedBy = actor; record.ApprovedAt = now; record.Status = record.EffectiveFrom > now ? OperationalStatus.Scheduled : OperationalStatus.Applied;
        foreach (var prior in superseded) prior.EffectiveUntil = record.EffectiveFrom;
        if (record.Status == OperationalStatus.Applied) await MaterializeEffectivePriceAsync(record.Variant, now, [record], cancellationToken);
        Audit("PRICE_APPROVED", "PriceRecord", record.Id.ToString(), actor, before, new { record.Status, record.ApprovedBy }, reason);
        await db.SaveChangesAsync(cancellationToken);
        if (record.Status == OperationalStatus.Applied) await InvalidatePricingAsync(record.VariantId, record.Variant.Product.Slug);
        return MapPrice(record);
    }

    public async Task<object> ApprovePricingAsync(Guid id, string actor, string reason, CancellationToken cancellationToken)
    {
        var recommendation = await db.PriceRecommendations.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (recommendation is null) return await ApprovePriceAsync(id, actor, reason, cancellationToken);
        if (recommendation.Status is not (OperationalStatus.Proposed or OperationalStatus.ReviewRequired)) throw CommerceErrors.Conflict("approval_state_invalid", "The recommendation is not awaiting approval.");
        var variant = await db.ProductVariants.Include(x => x.Product).SingleAsync(x => x.Id == recommendation.VariantId, cancellationToken);
        var policy = await PolicyAsync(variant.Product.CategoryId, variant.Currency, cancellationToken);
        var errors = GuardrailErrors(variant.Price, recommendation.RecommendedPrice, variant.UnitCost, policy).ToArray();
        if (errors.Length > 0) throw CommerceErrors.Conflict("pricing_guardrail_violation", string.Join(" ", errors));
        var now = clock.GetUtcNow();
        var effectiveAt = recommendation.EffectiveAt ?? now;
        var overlaps = await OverlappingPricesAsync(variant.Id, PriceKind.Regular, effectiveAt, null, null, cancellationToken);
        var superseded = overlaps.Where(x => x.Status == OperationalStatus.Applied && x.EffectiveFrom <= effectiveAt).ToList();
        if (overlaps.Except(superseded).Any()) throw CommerceErrors.Conflict("price_interval_overlap", "An incompatible regular price interval already exists.");
        var record = new PriceRecord { Id = Guid.CreateVersion7(), VariantId = variant.Id, Currency = variant.Currency, Price = recommendation.RecommendedPrice, CostAtTime = variant.UnitCost, EffectiveFrom = effectiveAt, Reason = reason, CreatedBy = $"recommendation:{recommendation.Id}", CreatedAt = now, ApprovedBy = actor, ApprovedAt = now, Source = "Recommendation", Revision = await NextRevisionAsync(variant.Id, cancellationToken), Kind = PriceKind.Regular, Status = effectiveAt > now ? OperationalStatus.Scheduled : OperationalStatus.Applied };
        foreach (var prior in superseded) prior.EffectiveUntil = effectiveAt;
        recommendation.Status = OperationalStatus.Approved; recommendation.ApprovedBy = actor;
        db.PriceRecords.Add(record);
        if (record.Status == OperationalStatus.Applied) await MaterializeEffectivePriceAsync(variant, now, [record], cancellationToken);
        Audit("PRICE_RECOMMENDATION_APPROVED", "PriceRecommendation", recommendation.Id.ToString(), actor, null, new { recommendation.Status, PriceRecordId = record.Id }, reason);
        await db.SaveChangesAsync(cancellationToken);
        if (record.Status == OperationalStatus.Applied) await InvalidatePricingAsync(variant.Id, variant.Product.Slug);
        return MapPrice(record);
    }

    public async Task<PriceSimulationDto> SimulatePriceAsync(PriceSimulationRequest request, CancellationToken cancellationToken)
    {
        if (request.CandidatePrice <= 0 || request.HorizonDays is < 1 or > 365) throw CommerceErrors.Validation("Candidate price and horizon are invalid.");
        var variant = await db.ProductVariants.AsNoTracking().Include(x => x.Inventory).SingleOrDefaultAsync(x => x.Id == request.VariantId, cancellationToken) ?? throw CommerceErrors.NotFound("Variant");
        var observations = await db.DemandObservations.AsNoTracking().Where(x => x.VariantId == request.VariantId && x.StockAvailable).OrderBy(x => x.PeriodStart).Take(365).ToListAsync(cancellationToken);
        if (observations.Count < 4 || observations.Select(x => x.EffectivePrice).Distinct().Count() < 2)
            return new(variant.Id, variant.Price, request.CandidatePrice, false, "Insufficient stocked observations at multiple prices.", null, null, null, OperationsCalculations.GrossMarginPercent(request.CandidatePrice, variant.UnitCost), null, null, null, null, null, "Unavailable", ["No live data was mutated.", "Predictive output requires at least four stocked observations at two or more prices."]);
        var first = observations.First(); var last = observations.Last();
        var elasticity = OperationsCalculations.ArcElasticity(first.EffectivePrice, last.EffectivePrice, first.UnitsOrdered, last.UnitsOrdered);
        if (elasticity is null) return new(variant.Id, variant.Price, request.CandidatePrice, false, "Elasticity cannot be estimated from the available observations.", null, null, null, OperationsCalculations.GrossMarginPercent(request.CandidatePrice, variant.UnitCost), null, null, null, null, null, "Low", ["No live data was mutated."]);
        var daily = observations.Sum(x => x.UnitsOrdered) / Math.Max(1m, (decimal)observations.Sum(x => x.PeriodDuration.TotalDays));
        var ratio = request.CandidatePrice / variant.Price;
        var predicted = Math.Max(0, daily * request.HorizonDays * (decimal)Math.Pow((double)ratio, (double)elasticity.Value));
        var revenue = OperationsCalculations.Revenue(request.CandidatePrice, predicted);
        var grossProfit = OperationsCalculations.GrossProfit(request.CandidatePrice, variant.UnitCost, predicted);
        var dailyPredicted = predicted / request.HorizonDays;
        var days = OperationsCalculations.DaysOfSupply(variant.Inventory.QuantityOnHand, dailyPredicted);
        return new(variant.Id, variant.Price, request.CandidatePrice, true, null, decimal.Round(predicted, 2), revenue, grossProfit, OperationsCalculations.GrossMarginPercent(request.CandidatePrice, variant.UnitCost), days, days is null ? null : clock.GetUtcNow().AddDays((double)days.Value), decimal.Round(predicted * .8m, 2), decimal.Round(predicted * 1.2m, 2), elasticity, "Low - observational heuristic", ["Constant-elasticity scenario, not a causal estimate.", "Promotions, traffic, seasonality, stock availability, and marketing may confound observed changes.", "No live data was mutated."]);
    }

    public async Task<IReadOnlyList<PriceRecommendationDto>> GetPriceRecommendationsAsync(CancellationToken cancellationToken) =>
        (await db.PriceRecommendations.AsNoTracking().OrderByDescending(x => x.GeneratedAt).Take(200).ToListAsync(cancellationToken)).Select(MapRecommendation).ToList();

    public async Task<IReadOnlyList<DemandObservationDto>> GetDemandAsync(Guid? variantId, DateTimeOffset from, CancellationToken cancellationToken)
    {
        var query = db.DemandObservations.AsNoTracking().Where(x => x.PeriodStart >= from);
        if (variantId is not null) query = query.Where(x => x.VariantId == variantId);
        return await query.OrderByDescending(x => x.PeriodStart).Take(1000).Select(x => new DemandObservationDto(x.VariantId, x.PeriodStart, x.ProductViews, x.SearchImpressions, x.AddToCartCount, x.Orders, x.UnitsOrdered, x.UnitsFulfilled, x.Revenue, x.EffectivePrice, x.PromotionActive, x.StockAvailable, x.Channel, x.Region, x.ProductViews == 0 ? null : decimal.Round((decimal)x.Orders / x.ProductViews * 100m, 4), null)).ToListAsync(cancellationToken);
    }

    public async Task<ForecastDto> GenerateForecastAsync(ForecastRequest request, string actor, CancellationToken cancellationToken)
    {
        if (request.HorizonDays is < 1 or > 365 || request.InputWindowDays is < 3 or > 730) throw CommerceErrors.Validation("Forecast horizon or input window is invalid.");
        if (!Enum.TryParse<ForecastModel>(request.Model, true, out var model) || model is not (ForecastModel.Naive or ForecastModel.MovingAverage or ForecastModel.ExponentialSmoothing)) throw CommerceErrors.Validation("Unsupported forecast model.");
        var from = clock.GetUtcNow().AddDays(-request.InputWindowDays);
        var values = await db.DemandObservations.AsNoTracking().Where(x => x.VariantId == request.VariantId && x.PeriodStart >= from).OrderBy(x => x.PeriodStart).Select(x => (decimal)x.UnitsOrdered).ToListAsync(cancellationToken);
        if (values.Count < 3) throw CommerceErrors.Validation("At least three demand observations are required.");
        var daily = model switch { ForecastModel.Naive => values[^1], ForecastModel.ExponentialSmoothing => ExponentialSmooth(values, .35m), _ => values.TakeLast(Math.Min(7, values.Count)).Average() };
        var standardDeviation = StandardDeviation(values);
        var predicted = Math.Max(0, daily * request.HorizonDays);
        var spread = 1.96m * standardDeviation * (decimal)Math.Sqrt(request.HorizonDays);
        var backtestForecast = Enumerable.Range(1, values.Count - 1).Select(i => model == ForecastModel.Naive ? values[i - 1] : values.Take(i).TakeLast(Math.Min(7, i)).Average()).ToArray();
        var metrics = OperationsCalculations.ForecastMetrics(values.Skip(1).ToArray(), backtestForecast);
        var forecast = new DemandForecast { Id = Guid.CreateVersion7(), VariantId = request.VariantId, HorizonStart = clock.GetUtcNow(), HorizonDays = request.HorizonDays, PredictedUnits = decimal.Round(predicted, 2), ConfidenceLower = Math.Max(0, decimal.Round(predicted - spread, 2)), ConfidenceUpper = decimal.Round(predicted + spread, 2), ConfidenceLevel = .95m, Model = model, ModelVersion = $"{model.ToString().ToLowerInvariant()}-v1", InputWindowDays = request.InputWindowDays, GeneratedAt = clock.GetUtcNow(), Mae = metrics.Mae, Rmse = metrics.Rmse, Wape = metrics.Wape, Bias = metrics.Bias };
        db.DemandForecasts.Add(forecast); Audit("FORECAST_GENERATED", "DemandForecast", forecast.Id.ToString(), actor, null, forecast, "Forecast generated from stored demand observations."); await db.SaveChangesAsync(cancellationToken);
        return MapForecast(forecast);
    }

    public async Task<IReadOnlyList<WarehouseStockDto>> GetInventoryAsync(CancellationToken cancellationToken)
    {
        var rows = await db.WarehouseStocks.AsNoTracking().Join(db.Warehouses, s => s.WarehouseId, w => w.Id, (s, w) => new { s, w }).Join(db.ProductVariants, x => x.s.VariantId, v => v.Id, (x, v) => new { x.s, x.w, v.Sku }).OrderBy(x => x.Sku).ToListAsync(cancellationToken);
        var demand = await AverageDailyDemandAsync(rows.Select(x => x.s.VariantId).Distinct().ToArray(), 30, cancellationToken);
        return rows.Select(x => MapStock(x.s, x.w.Name, null, x.Sku, demand.GetValueOrDefault(x.s.VariantId))).ToList();
    }

    public async Task<IReadOnlyList<InventoryLedgerEntryDto>> AdjustInventoryAsync(InventoryAdjustmentRequest request, string actor, CancellationToken cancellationToken)
    {
        if (request.QuantityDelta == 0 || Math.Abs(request.QuantityDelta) > 100_000) throw CommerceErrors.Validation("Adjustment quantity is invalid.");
        if (!Enum.TryParse<InventoryMovementReason>(request.Reason, true, out var reason) || reason is InventoryMovementReason.TransferIn or InventoryMovementReason.TransferOut or InventoryMovementReason.OrderReserved or InventoryMovementReason.ReservationReleased or InventoryMovementReason.OrderFulfilled) throw CommerceErrors.Validation("Use an allowed adjustment reason.");
        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        await db.LockInventoryAsync([request.VariantId], cancellationToken);
        var stock = await db.WarehouseStocks.SingleOrDefaultAsync(x => x.VariantId == request.VariantId && x.WarehouseId == request.WarehouseId, cancellationToken) ?? throw CommerceErrors.NotFound("Warehouse stock");
        var inventory = await db.Inventory.Include(x => x.Variant).ThenInclude(x => x.Product).SingleAsync(x => x.VariantId == request.VariantId, cancellationToken);
        if (stock.OnHand + request.QuantityDelta < stock.Reserved + stock.Unavailable) throw CommerceErrors.Conflict("insufficient_inventory", "The adjustment would reduce stock below reserved or unavailable units.");
        var before = new { stock.OnHand, inventory.QuantityOnHand };
        var now = clock.GetUtcNow();
        IReadOnlyList<(InventoryBalance Balance, int Quantity)> consumed = [];
        if (request.QuantityDelta < 0)
            consumed = await InventoryBalanceOperations.ConsumeAvailableAsync(db, stock, -request.QuantityDelta, now, cancellationToken);
        else
        {
            var balance = await InventoryBalanceOperations.EnsureUnscopedAvailableAsync(db, stock, now, cancellationToken);
            balance.Quantity += request.QuantityDelta; balance.UpdatedAt = now;
        }
        stock.OnHand += request.QuantityDelta; stock.UpdatedAt = clock.GetUtcNow(); inventory.QuantityOnHand += request.QuantityDelta; inventory.UpdatedAt = clock.GetUtcNow();
        var ledgerEntries = new List<InventoryLedgerEntry>();
        if (request.QuantityDelta > 0)
        {
            var ledger = Ledger(request.VariantId, request.WarehouseId, request.QuantityDelta, reason, "Adjustment", request.ReferenceId, actor);
            ledger.State = InventoryState.Available;
            ledgerEntries.Add(ledger);
        }
        else
        {
            foreach (var allocation in consumed)
            {
                var ledger = Ledger(request.VariantId, request.WarehouseId, -allocation.Quantity, reason, "Adjustment", request.ReferenceId, actor);
                ledger.State = InventoryState.Available; ledger.LotId = allocation.Balance.LotId; ledger.LocationId = allocation.Balance.LocationId;
                ledgerEntries.Add(ledger);
            }
        }
        db.InventoryLedgerEntries.AddRange(ledgerEntries);
        Audit("INVENTORY_ADJUSTED", "WarehouseStock", $"{request.WarehouseId}:{request.VariantId}", actor, before, new { stock.OnHand, inventory.QuantityOnHand }, request.Note);
        await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken); await InvalidatePricingAsync(request.VariantId, inventory.Variant.Product.Slug);
        return ledgerEntries.Select(MapLedger).ToList();
    }

    public async Task<IReadOnlyList<ReplenishmentDto>> GetReplenishmentAsync(CancellationToken cancellationToken) => (await db.ReplenishmentRecommendations.AsNoTracking().OrderByDescending(x => x.GeneratedAt).Take(300).ToListAsync(cancellationToken)).Select(MapReplenishment).ToList();

    public async Task<IReadOnlyList<ReplenishmentDto>> GenerateReplenishmentAsync(string actor, CancellationToken cancellationToken)
    {
        var stocks = await db.WarehouseStocks.ToListAsync(cancellationToken);
        var demand = await AverageDailyDemandAsync(stocks.Select(x => x.VariantId).Distinct().ToArray(), 30, cancellationToken);
        var now = clock.GetUtcNow();
        var generated = new List<ReplenishmentRecommendation>();
        foreach (var stock in stocks)
        {
            var average = demand.GetValueOrDefault(stock.VariantId);
            if (average <= 0) continue;
            var reorderPoint = OperationsCalculations.ReorderPoint(average, stock.SupplierLeadTimeDays, stock.SafetyStock);
            var available = OperationsCalculations.AvailableToSell(stock.OnHand, stock.Reserved, stock.SafetyStock, stock.Unavailable) + stock.Inbound;
            if (available > reorderPoint) continue;
            var target = (int)Math.Ceiling(average * Math.Max(14, stock.SupplierLeadTimeDays * 2) + stock.SafetyStock);
            var quantity = Math.Max(1, target - available);
            var recommendation = new ReplenishmentRecommendation { Id = Guid.CreateVersion7(), VariantId = stock.VariantId, WarehouseId = stock.WarehouseId, RecommendedQuantity = quantity, AverageDailyDemand = average, ReorderPoint = reorderPoint, ProjectedStockoutAt = available <= 0 ? now : now.AddDays((double)(available / average)), ExplanationJson = JsonSerializer.Serialize(new[] { "Available stock is at or below the reorder point.", "Quantity targets lead-time coverage plus safety stock." }), ModelVersion = "reorder-point-v1", Status = OperationalStatus.ReviewRequired, GeneratedAt = now };
            db.ReplenishmentRecommendations.Add(recommendation); generated.Add(recommendation);
        }
        Audit("REPLENISHMENT_GENERATED", "ReplenishmentRecommendation", "batch", actor, null, new { Count = generated.Count }, "Generated from observed demand and current warehouse positions.");
        await db.SaveChangesAsync(cancellationToken);
        return generated.Select(MapReplenishment).ToList();
    }

    public async Task<ReplenishmentDto> ApproveReplenishmentAsync(Guid id, string actor, string reason, CancellationToken cancellationToken)
    {
        var recommendation = await db.ReplenishmentRecommendations.SingleOrDefaultAsync(x => x.Id == id, cancellationToken) ?? throw CommerceErrors.NotFound("Replenishment recommendation");
        if (recommendation.Status != OperationalStatus.ReviewRequired) throw CommerceErrors.Conflict("approval_state_invalid", "The replenishment recommendation is not awaiting approval.");
        recommendation.Status = OperationalStatus.Approved; recommendation.ApprovedBy = actor;
        Audit("REPLENISHMENT_APPROVED", "ReplenishmentRecommendation", id.ToString(), actor, null, new { recommendation.Status, recommendation.ApprovedBy }, reason);
        await db.SaveChangesAsync(cancellationToken);
        return MapReplenishment(recommendation);
    }

    public async Task<IReadOnlyList<PromotionDto>> GetPromotionsAsync(CancellationToken cancellationToken) => (await db.Promotions.AsNoTracking().OrderByDescending(x => x.CreatedAt).ToListAsync(cancellationToken)).Select(MapPromotion).ToList();

    public async Task<PromotionDto> CreatePromotionAsync(PromotionRequest request, string actor, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 200 || request.EndsAt <= request.StartsAt || request.DiscountPercent is <= 0 or > 100) throw CommerceErrors.Validation("Promotion request is invalid.");
        var overlap = await db.Promotions.AnyAsync(x => (x.CategoryId == null || request.CategoryId == null || x.CategoryId == request.CategoryId) && x.Status != OperationalStatus.Rejected && x.Status != OperationalStatus.Expired && x.StartsAt < request.EndsAt && x.EndsAt > request.StartsAt, cancellationToken);
        if (overlap) throw CommerceErrors.Conflict("promotion_overlap", "An overlapping promotion already exists for this scope.");
        var promotion = new Promotion { Id = Guid.CreateVersion7(), Name = request.Name.Trim(), CategoryId = request.CategoryId, StartsAt = request.StartsAt, EndsAt = request.EndsAt, DiscountPercent = request.DiscountPercent, Status = OperationalStatus.ReviewRequired, CreatedBy = actor, CreatedAt = clock.GetUtcNow() };
        db.Promotions.Add(promotion); Audit("PROMOTION_PROPOSED", "Promotion", promotion.Id.ToString(), actor, null, promotion, promotion.Name); await db.SaveChangesAsync(cancellationToken); return MapPromotion(promotion);
    }

    public async Task<PromotionDto> ApprovePromotionAsync(Guid id, string actor, string reason, CancellationToken cancellationToken)
    {
        var promotion = await db.Promotions.SingleOrDefaultAsync(x => x.Id == id, cancellationToken) ?? throw CommerceErrors.NotFound("Promotion");
        if (promotion.Status != OperationalStatus.ReviewRequired) throw CommerceErrors.Conflict("approval_state_invalid", "Promotion is not awaiting approval.");
        if (promotion.CreatedBy == actor) throw new CommerceException("maker_checker_required", "The maker cannot approve this promotion.", 403);
        var now = clock.GetUtcNow();
        if (promotion.EndsAt <= now) throw CommerceErrors.Conflict("promotion_expired", "An expired promotion cannot be approved.");
        var variantsQuery = db.ProductVariants.Include(x => x.Product).Where(x => x.IsActive);
        if (promotion.CategoryId is not null) variantsQuery = variantsQuery.Where(x => x.Product.CategoryId == promotion.CategoryId);
        var variants = await variantsQuery.ToListAsync(cancellationToken);
        var planned = new List<(ProductVariant Variant, decimal Price)>();
        foreach (var variant in variants)
        {
            var price = decimal.Round(variant.Price * (1m - promotion.DiscountPercent / 100m), 4);
            var policy = await PolicyAsync(variant.Product.CategoryId, variant.Currency, cancellationToken);
            var errors = GuardrailErrors(variant.Price, price, variant.UnitCost, policy).ToArray();
            if (errors.Length > 0) throw CommerceErrors.Conflict("promotion_guardrail_violation", $"{variant.Sku}: {string.Join(" ", errors)}");
            if ((await OverlappingPricesAsync(variant.Id, PriceKind.Promotion, promotion.StartsAt, promotion.EndsAt, null, cancellationToken)).Count > 0) throw CommerceErrors.Conflict("price_interval_overlap", $"{variant.Sku} already has a promotion price in this interval.");
            planned.Add((variant, price));
        }
        promotion.ApprovedBy = actor; promotion.Status = promotion.StartsAt > now ? OperationalStatus.Scheduled : OperationalStatus.Applied;
        foreach (var item in planned)
        {
            var record = new PriceRecord { Id = Guid.CreateVersion7(), VariantId = item.Variant.Id, Currency = item.Variant.Currency, Price = item.Price, CompareAtPrice = item.Variant.Price, CostAtTime = item.Variant.UnitCost, EffectiveFrom = promotion.StartsAt, EffectiveUntil = promotion.EndsAt, Reason = reason, PromotionId = promotion.Id, CreatedBy = promotion.CreatedBy, CreatedAt = now, ApprovedBy = actor, ApprovedAt = now, Source = "Promotion", Revision = await NextRevisionAsync(item.Variant.Id, cancellationToken), Kind = PriceKind.Promotion, Status = promotion.Status };
            db.PriceRecords.Add(record);
            if (record.Status == OperationalStatus.Applied) await MaterializeEffectivePriceAsync(item.Variant, now, [record], cancellationToken);
        }
        Audit("PROMOTION_APPROVED", "Promotion", id.ToString(), actor, null, new { promotion.Status, promotion.ApprovedBy, VariantCount = planned.Count }, reason);
        await db.SaveChangesAsync(cancellationToken);
        foreach (var item in planned.Where(x => promotion.Status == OperationalStatus.Applied)) await InvalidatePricingAsync(item.Variant.Id, item.Variant.Product.Slug);
        return MapPromotion(promotion);
    }

    public async Task<IReadOnlyList<AuditEntryDto>> GetAuditAsync(int pageSize, CancellationToken cancellationToken) => (await db.OperationsAuditEntries.AsNoTracking().OrderByDescending(x => x.CreatedAt).Take(Math.Clamp(pageSize, 1, 200)).ToListAsync(cancellationToken)).Select(MapAudit).ToList();
    public async Task<IReadOnlyList<OperationalAlertDto>> GetAlertsAsync(CancellationToken cancellationToken) => (await db.OperationalAlerts.AsNoTracking().OrderByDescending(x => x.UpdatedAt).Take(300).ToListAsync(cancellationToken)).Select(MapAlert).ToList();

    public async Task<OperationalAlertDto> AcknowledgeAlertAsync(Guid id, string actor, CancellationToken cancellationToken)
    {
        var alert = await db.OperationalAlerts.SingleOrDefaultAsync(x => x.Id == id, cancellationToken) ?? throw CommerceErrors.NotFound("Alert"); alert.Status = AlertStatus.Acknowledged; alert.AcknowledgedBy = actor; alert.UpdatedAt = clock.GetUtcNow(); Audit("ALERT_ACKNOWLEDGED", "OperationalAlert", id.ToString(), actor, null, new { alert.Status }, "Alert acknowledged."); await db.SaveChangesAsync(cancellationToken); return MapAlert(alert);
    }

    public async Task<IReadOnlyList<ApprovalQueueItemDto>> GetApprovalsAsync(CancellationToken cancellationToken)
    {
        var prices = await db.PriceRecords.AsNoTracking().Where(x => x.Status == OperationalStatus.ReviewRequired).OrderBy(x => x.CreatedAt).ToListAsync(cancellationToken);
        var recommendations = await db.PriceRecommendations.AsNoTracking().Where(x => x.Status == OperationalStatus.Proposed || x.Status == OperationalStatus.ReviewRequired).OrderBy(x => x.GeneratedAt).ToListAsync(cancellationToken);
        var replenishment = await db.ReplenishmentRecommendations.AsNoTracking().Where(x => x.Status == OperationalStatus.ReviewRequired).OrderBy(x => x.GeneratedAt).ToListAsync(cancellationToken);
        var promotions = await db.Promotions.AsNoTracking().Where(x => x.Status == OperationalStatus.ReviewRequired).OrderBy(x => x.CreatedAt).ToListAsync(cancellationToken);
        return prices.Select(x => new ApprovalQueueItemDto(x.Id, "PriceRecord", x.VariantId.ToString(), $"{x.Currency} {x.Price:0.00} from {x.EffectiveFrom:u}", x.Status.ToString(), x.CreatedAt, x.CreatedBy))
            .Concat(recommendations.Select(x => new ApprovalQueueItemDto(x.Id, "PriceRecommendation", x.VariantId.ToString(), $"{x.CurrentPrice:0.00} to {x.RecommendedPrice:0.00}", x.Status.ToString(), x.GeneratedAt, null)))
            .Concat(replenishment.Select(x => new ApprovalQueueItemDto(x.Id, "Replenishment", x.VariantId.ToString(), $"{x.RecommendedQuantity} units", x.Status.ToString(), x.GeneratedAt, null)))
            .Concat(promotions.Select(x => new ApprovalQueueItemDto(x.Id, "Promotion", x.Id.ToString(), $"{x.Name}: {x.DiscountPercent:0.##}%", x.Status.ToString(), x.CreatedAt, x.CreatedBy)))
            .OrderBy(x => x.CreatedAt).ToList();
    }

    public async Task<BulkOperationPreviewDto> BulkPricesAsync(BulkPriceRequest request, string actor, bool apply, CancellationToken cancellationToken)
    {
        if (request.Items.Count is < 1 or > 100 || request.Items.Select(x => x.VariantId).Distinct().Count() != request.Items.Count) throw CommerceErrors.Validation("Bulk request must contain 1-100 unique variants.");
        var variants = await db.ProductVariants.Include(x => x.Product).Where(x => request.Items.Select(i => i.VariantId).Contains(x.Id)).ToDictionaryAsync(x => x.Id, cancellationToken);
        var results = new List<BulkOperationItemDto>();
        foreach (var item in request.Items)
        {
            if (!variants.TryGetValue(item.VariantId, out var variant)) { results.Add(new(item.VariantId, false, ["Variant not found."], false)); continue; }
            var policy = await PolicyAsync(variant.Product.CategoryId, variant.Currency, cancellationToken); var errors = GuardrailErrors(variant.Price, item.Price, variant.UnitCost, policy).ToList(); var overlap = await db.PriceRecords.AnyAsync(x => x.VariantId == item.VariantId && x.Kind == PriceKind.Regular && x.Status != OperationalStatus.Rejected && x.Status != OperationalStatus.Expired && x.EffectiveFrom < (item.EffectiveUntil ?? DateTimeOffset.MaxValue) && (x.EffectiveUntil == null || x.EffectiveUntil > item.EffectiveFrom), cancellationToken); if (overlap) errors.Add("Overlapping regular price interval.");
            var change = variant.Price == 0 ? 100 : Math.Abs((item.Price - variant.Price) / variant.Price * 100); results.Add(new(item.VariantId, errors.Count == 0, errors, change >= policy.ApprovalThresholdPercent));
        }
        if (apply && results.Any(x => !x.Valid)) throw CommerceErrors.Conflict("bulk_validation_failed", "Bulk price update contains invalid rows. No changes were applied.");
        if (apply)
        {
            await using var transaction = await db.BeginTransactionAsync(cancellationToken);
            foreach (var item in request.Items) await SchedulePriceAsync(new(item.VariantId, item.Price, null, item.EffectiveFrom, item.EffectiveUntil, "Regular", item.Reason), actor, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        return new(results, results.Count(x => x.Valid), results.Count(x => !x.Valid), apply);
    }

    public async Task ApplyDuePricesAsync(string actor, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        await db.LockPriceActivationAsync(cancellationToken);
        var due = await db.PriceRecords.Include(x => x.Variant).ThenInclude(x => x.Product)
            .Where(x => (x.Status == OperationalStatus.Scheduled && x.EffectiveFrom <= now) || (x.Status == OperationalStatus.Applied && x.EffectiveUntil <= now))
            .OrderBy(x => x.EffectiveFrom).ThenBy(x => x.Revision).ToListAsync(cancellationToken);
        if (due.Count == 0) { await transaction.CommitAsync(cancellationToken); return; }
        foreach (var record in due)
        {
            var prior = record.Status;
            record.Status = record.EffectiveUntil <= now ? OperationalStatus.Expired : OperationalStatus.Applied;
            if (prior == OperationalStatus.Scheduled && record.Status == OperationalStatus.Applied)
                Audit("PRICE_APPLIED", "PriceRecord", record.Id.ToString(), actor, new { Status = prior }, new { record.Price, record.Revision, record.Status }, "Scheduled price became effective.");
        }
        foreach (var group in due.GroupBy(x => x.VariantId))
        {
            await MaterializeEffectivePriceAsync(group.First().Variant, now, group, cancellationToken);
        }
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        foreach (var item in due.Select(x => new { x.VariantId, x.Variant.Product.Slug }).Distinct()) await InvalidatePricingAsync(item.VariantId, item.Slug);
    }

    private async Task<PricingPolicy> PolicyAsync(Guid categoryId, string currency, CancellationToken ct) => await db.PricingPolicies.AsNoTracking().Where(x => x.IsActive && x.Currency == currency && (x.CategoryId == categoryId || x.CategoryId == null)).OrderByDescending(x => x.CategoryId != null).FirstOrDefaultAsync(ct) ?? new PricingPolicy { Currency = currency, MaximumChangePercent = 25, MaximumMarkdownPercent = 40, ApprovalThresholdPercent = 10, EnforceCostFloor = true };
    private static IEnumerable<string> GuardrailErrors(decimal current, decimal candidate, decimal? cost, PricingPolicy policy) { if (candidate <= 0) yield return "Price must be positive."; if (policy.MinimumPrice is not null && candidate < policy.MinimumPrice) yield return "Below minimum price."; if (policy.MaximumPrice is not null && candidate > policy.MaximumPrice) yield return "Above maximum price."; if (policy.EnforceCostFloor && cost is not null && candidate < cost) yield return "Below cost floor."; var margin = OperationsCalculations.GrossMarginPercent(candidate, cost); if (policy.MinimumGrossMarginPercent is not null && margin is not null && margin < policy.MinimumGrossMarginPercent) yield return "Below minimum permitted margin."; var markdown = OperationsCalculations.MarkdownPercent(current, candidate); if (markdown is not null && markdown > policy.MaximumMarkdownPercent) yield return "Exceeds maximum markdown."; var change = current <= 0 ? 100 : Math.Abs((candidate - current) / current * 100); if (change > policy.MaximumChangePercent) yield return "Exceeds maximum permitted price change."; }
    private async Task<int> NextRevisionAsync(Guid variantId, CancellationToken ct) => (await db.PriceRecords.Where(x => x.VariantId == variantId).MaxAsync(x => (int?)x.Revision, ct) ?? 0) + 1;
    private async Task<List<PriceRecord>> OverlappingPricesAsync(Guid variantId, PriceKind kind, DateTimeOffset from, DateTimeOffset? until, Guid? excludedId, CancellationToken ct) => await db.PriceRecords.Where(x => x.VariantId == variantId && x.Kind == kind && (excludedId == null || x.Id != excludedId.Value) && x.Status != OperationalStatus.Rejected && x.Status != OperationalStatus.Expired && x.EffectiveFrom < (until ?? DateTimeOffset.MaxValue) && (x.EffectiveUntil == null || x.EffectiveUntil > from)).ToListAsync(ct);
    private static void ValidatePriceRequest(PriceScheduleRequest x) { if (x.Price <= 0 || x.Price > 10_000_000 || x.CompareAtPrice < x.Price || x.EffectiveUntil <= x.EffectiveFrom || string.IsNullOrWhiteSpace(x.Reason) || x.Reason.Length > 500) throw CommerceErrors.Validation("Price schedule is invalid."); }
    private void Audit(string eventType, string resourceType, string resourceId, string actor, object? before, object? after, string reason) => db.OperationsAuditEntries.Add(new OperationsAuditEntry { Id = Guid.CreateVersion7(), EventType = eventType, ResourceType = resourceType, ResourceId = resourceId, ActorId = actor, BeforeJson = before is null ? null : JsonSerializer.Serialize(before), AfterJson = after is null ? null : JsonSerializer.Serialize(after), Reason = reason[..Math.Min(reason.Length, 500)], CreatedAt = clock.GetUtcNow() });
    private InventoryLedgerEntry Ledger(Guid variantId, Guid warehouseId, int delta, InventoryMovementReason reason, string referenceType, string referenceId, string actor) => new() { Id = Guid.CreateVersion7(), VariantId = variantId, WarehouseId = warehouseId, QuantityDelta = delta, Reason = reason, ReferenceType = referenceType, ReferenceId = referenceId[..Math.Min(referenceId.Length, 120)], CreatedBy = actor, CreatedAt = clock.GetUtcNow() };
    private async Task<Dictionary<Guid, decimal>> AverageDailyDemandAsync(Guid[] variantIds, int days, CancellationToken ct) { var from = clock.GetUtcNow().AddDays(-days); var rows = await db.DemandObservations.AsNoTracking().Where(x => variantIds.Contains(x.VariantId) && x.PeriodStart >= from).Select(x => new { x.VariantId, x.UnitsOrdered, x.PeriodDuration }).ToListAsync(ct); return rows.GroupBy(x => x.VariantId).ToDictionary(x => x.Key, x => x.Sum(v => v.UnitsOrdered) / Math.Max(1m, (decimal)x.Sum(v => v.PeriodDuration.TotalDays))); }
    private static InventoryHealth ClassifyInventory(int onHand, int safety, int inbound, decimal averageDailyDemand, int leadDays) { if (onHand <= 0) return InventoryHealth.OutOfStock; var available = Math.Max(0, onHand - safety); var reorder = OperationsCalculations.ReorderPoint(averageDailyDemand, leadDays, safety); if (available <= reorder && inbound == 0) return InventoryHealth.StockoutRisk; if (available <= safety) return InventoryHealth.LowStock; if (averageDailyDemand > 0 && available / averageDailyDemand > 90) return InventoryHealth.ExcessStock; if (averageDailyDemand == 0 && onHand > 0) return InventoryHealth.SlowMoving; return InventoryHealth.Healthy; }
    private static decimal ExponentialSmooth(IReadOnlyList<decimal> values, decimal alpha) { var result = values[0]; for (var i = 1; i < values.Count; i++) result = alpha * values[i] + (1 - alpha) * result; return result; }
    private static decimal StandardDeviation(IReadOnlyList<decimal> values) { if (values.Count < 2) return 0; var avg = values.Average(); return (decimal)Math.Sqrt((double)(values.Sum(x => (x - avg) * (x - avg)) / (values.Count - 1))); }
    private async Task MaterializeEffectivePriceAsync(ProductVariant variant, DateTimeOffset at, IEnumerable<PriceRecord> pending, CancellationToken ct)
    {
        var persisted = await db.PriceRecords.Where(x => x.VariantId == variant.Id && x.Status == OperationalStatus.Applied && x.EffectiveFrom <= at && (x.EffectiveUntil == null || x.EffectiveUntil > at)).ToListAsync(ct);
        var effective = persisted.Concat(pending).DistinctBy(x => x.Id).Where(x => x.Status == OperationalStatus.Applied && x.EffectiveFrom <= at && (x.EffectiveUntil == null || x.EffectiveUntil > at)).OrderByDescending(x => PricePriority(x.Kind)).ThenByDescending(x => x.Revision).FirstOrDefault();
        if (effective is not null) variant.Price = effective.Price;
    }
    private async Task InvalidatePricingAsync(Guid variantId, string slug) { foreach (var tag in new[] { "products", "homepage", "search-suggestions", $"inventory:{variantId}", $"product:{slug.ToLowerInvariant()}" }) await cache.RemoveByTagAsync(tag, CancellationToken.None); }
    private static int PricePriority(PriceKind kind) => kind switch { PriceKind.Promotion => 3, PriceKind.Markdown => 2, _ => 1 };
    private static PriceRecordDto MapPrice(PriceRecord x) => new(x.Id, x.VariantId, new(x.Price, x.Currency), x.CompareAtPrice is null ? null : new(x.CompareAtPrice.Value, x.Currency), x.CostAtTime is null ? null : new(x.CostAtTime.Value, x.Currency), x.EffectiveFrom, x.EffectiveUntil, x.Reason, x.Kind.ToString(), x.Status.ToString(), x.Source, x.Revision, x.CreatedBy, x.CreatedAt, x.ApprovedBy);
    private static PriceRecommendationDto MapRecommendation(PriceRecommendation x) => new(x.Id, x.VariantId, x.CurrentPrice, x.RecommendedPrice, x.PredictedDemand, x.PredictedRevenue, x.PredictedGrossProfit, x.ConfidenceLower, x.ConfidenceUpper, x.ConfidenceLevel, JsonSerializer.Deserialize<string[]>(x.ReasonCodesJson) ?? [], x.ModelVersion, x.GeneratedAt, x.Status.ToString(), x.ApprovedBy);
    private static ForecastDto MapForecast(DemandForecast x) => new(x.Id, x.VariantId, x.HorizonStart, x.HorizonDays, x.PredictedUnits, x.ConfidenceLower, x.ConfidenceUpper, x.ConfidenceLevel, x.Model.ToString(), x.ModelVersion, x.InputWindowDays, x.GeneratedAt, x.Mae, x.Rmse, x.Wape, x.Bias);
    private static WarehouseStockDto MapStock(WarehouseStock x, string warehouse, string? location, string sku, decimal avg) { var ats = OperationsCalculations.AvailableToSell(x.OnHand, x.Reserved, x.SafetyStock, x.Unavailable); var days = OperationsCalculations.DaysOfSupply(ats, avg); return new(x.WarehouseId, warehouse, location, x.VariantId, sku, x.OnHand, x.Reserved, x.SafetyStock, x.Unavailable, x.Inbound, ats, x.SupplierLeadTimeDays, ClassifyInventory(x.OnHand, x.SafetyStock, x.Inbound, avg, x.SupplierLeadTimeDays).ToString(), avg > 0 ? avg : null, days, OperationsCalculations.ReorderPoint(avg, x.SupplierLeadTimeDays, x.SafetyStock), days is null ? null : DateTimeOffset.UtcNow.AddDays((double)days.Value)); }
    private static InventoryLedgerEntryDto MapLedger(InventoryLedgerEntry x) => new(x.Id, x.VariantId, x.WarehouseId, x.LocationId, x.QuantityDelta, x.Reason.ToString(), x.ReferenceType, x.ReferenceId, x.CreatedBy, x.CreatedAt);
    private static ReplenishmentDto MapReplenishment(ReplenishmentRecommendation x) => new(x.Id, x.VariantId, x.WarehouseId, x.RecommendedQuantity, x.AverageDailyDemand, x.ReorderPoint, x.ProjectedStockoutAt, JsonSerializer.Deserialize<string[]>(x.ExplanationJson) ?? [], x.ModelVersion, x.Status.ToString(), x.GeneratedAt, x.ApprovedBy);
    private static PromotionDto MapPromotion(Promotion x) => new(x.Id, x.Name, x.CategoryId, x.StartsAt, x.EndsAt, x.DiscountPercent, x.Status.ToString(), x.CreatedBy, x.ApprovedBy, x.CreatedAt);
    private static OperationalAlertDto MapAlert(OperationalAlert x) => new(x.Id, x.Type, x.Severity, x.Title, x.Detail, x.Status.ToString(), x.CreatedAt, x.UpdatedAt);
    private static AuditEntryDto MapAudit(OperationsAuditEntry x) => new(x.Id, x.EventType, x.ResourceType, x.ResourceId, x.ActorId, x.Reason, x.CreatedAt, x.BeforeJson, x.AfterJson);
}
