using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Commerce.Contracts;
using Commerce.Domain;
using Microsoft.EntityFrameworkCore;

namespace Commerce.Application;

public sealed class WarehouseExecutionService(ICommerceDbContext db, IReadModelCache cache, TimeProvider clock)
{
    public async Task<IReadOnlyList<WarehouseTaskDto>> GetTasksAsync(Guid? warehouseId, CancellationToken ct)
    {
        var query = db.WarehouseTasks.AsNoTracking().AsQueryable();
        if (warehouseId is not null) query = query.Where(x => x.WarehouseId == warehouseId);
        return await query.OrderByDescending(x => x.Priority).ThenBy(x => x.CreatedAt).Take(500).Select(x => new WarehouseTaskDto(x.Id, x.Number, x.WarehouseId, x.Type.ToString(), x.Status.ToString(), x.Priority, x.VariantId, x.LotId, x.SourceLocationId, x.DestinationLocationId, x.InventoryState.ToString(), x.Quantity, x.CompletedQuantity, x.ReferenceType, x.ReferenceId, x.AssignedTo, x.CreatedAt)).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<CycleCountDto>> GetCycleCountsAsync(Guid? warehouseId, CancellationToken ct)
    {
        var query = db.CycleCounts.AsNoTracking().Include(x => x.Lines).AsQueryable();
        if (warehouseId is not null) query = query.Where(x => x.WarehouseId == warehouseId);
        return (await query.OrderByDescending(x => x.CreatedAt).Take(300).ToListAsync(ct)).Select(MapCount).ToList();
    }

    public async Task<IReadOnlyList<StockTransferDto>> GetStockTransfersAsync(Guid? warehouseId, CancellationToken ct)
    {
        var query = db.StockTransfers.AsNoTracking().Include(x => x.Lines).AsQueryable();
        if (warehouseId is not null) query = query.Where(x => x.FromWarehouseId == warehouseId || x.ToWarehouseId == warehouseId);
        return (await query.OrderByDescending(x => x.CreatedAt).Take(300).ToListAsync(ct)).Select(x => MapTransfer(x)).ToList();
    }

    public async Task<StockTransferDto> CreateStockTransferAsync(CreateStockTransferRequest request, string idempotencyKey, string actor, CancellationToken ct)
    {
        ValidateIdempotencyKey(idempotencyKey);
        if (request.FromWarehouseId == request.ToWarehouseId || request.Lines.Count is < 1 or > 200 || request.Lines.Any(x => x.Quantity < 1) || request.Lines.Select(x => new { x.VariantId, x.LotId }).Distinct().Count() != request.Lines.Count) throw CommerceErrors.Validation("Stock transfer warehouses or lines are invalid.");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request))));
        await using var transaction = await db.BeginTransactionAsync(ct);
        await db.LockStockTransferIdempotencyAsync($"create:{idempotencyKey}", ct);
        var existing = await db.StockTransfers.AsNoTracking().Include(x => x.Lines).SingleOrDefaultAsync(x => x.CreateIdempotencyKey == idempotencyKey, ct);
        if (existing is not null)
        {
            if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(existing.CreateRequestHash), Convert.FromHexString(hash))) throw CommerceErrors.Conflict("idempotency_key_reused", "The idempotency key was already used with a different request.");
            await transaction.CommitAsync(ct);
            return MapTransfer(existing, true);
        }
        if (await db.Warehouses.CountAsync(x => (x.Id == request.FromWarehouseId || x.Id == request.ToWarehouseId) && x.IsActive, ct) != 2) throw CommerceErrors.Validation("Both transfer warehouses must be active.");
        var variantIds = request.Lines.Select(x => x.VariantId).Distinct().ToArray();
        if (await db.ProductVariants.CountAsync(x => variantIds.Contains(x.Id), ct) != variantIds.Length) throw CommerceErrors.Validation("Every transfer line must reference a variant.");
        var lotIds = request.Lines.Where(x => x.LotId is not null).Select(x => x.LotId!.Value).Distinct().ToArray();
        var lots = lotIds.Length == 0 ? new Dictionary<Guid, Guid>() : await db.InventoryLots.Where(x => lotIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.VariantId, ct);
        if (request.Lines.Any(x => x.LotId is not null && (!lots.TryGetValue(x.LotId.Value, out var variantId) || variantId != x.VariantId))) throw CommerceErrors.Validation("Every transfer lot must belong to its variant.");
        var now = clock.GetUtcNow();
        var transfer = new StockTransfer { Id = Guid.CreateVersion7(), Number = $"TR-{now:yyyyMMdd}-{Guid.NewGuid():N}"[..24], FromWarehouseId = request.FromWarehouseId, ToWarehouseId = request.ToWarehouseId, Status = StockTransferStatus.Draft, CreatedBy = actor, CreatedAt = now, CreateIdempotencyKey = idempotencyKey, CreateRequestHash = hash };
        transfer.Lines.AddRange(request.Lines.Select(x => new StockTransferLine { Id = Guid.CreateVersion7(), VariantId = x.VariantId, LotId = x.LotId, Quantity = x.Quantity }));
        db.StockTransfers.Add(transfer);
        foreach (var line in transfer.Lines)
            db.WarehouseTasks.Add(new WarehouseTask { Id = Guid.CreateVersion7(), Number = $"WT-{now:yyyyMMdd}-{Guid.NewGuid():N}"[..24], WarehouseId = transfer.FromWarehouseId, Type = WarehouseTaskType.Transfer, Status = WarehouseTaskStatus.Open, Priority = 60, VariantId = line.VariantId, LotId = line.LotId, InventoryState = InventoryState.Available, Quantity = line.Quantity, ReferenceType = "StockTransferDispatch", ReferenceId = line.Id.ToString(), CreatedBy = actor, CreatedAt = now });
        Audit("STOCK_TRANSFER_CREATED", "StockTransfer", transfer.Id, actor, transfer.Number); await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); return MapTransfer(transfer);
    }

    public async Task<StockTransferDto> DispatchStockTransferAsync(Guid id, string idempotencyKey, string actor, CancellationToken ct)
    {
        ValidateIdempotencyKey(idempotencyKey);
        await using var transaction = await db.BeginTransactionAsync(ct);
        await db.LockStockTransferIdempotencyAsync($"dispatch:{idempotencyKey}", ct);
        await db.LockStockTransferAsync(id, ct);
        var transfer = await db.StockTransfers.Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw CommerceErrors.NotFound("Stock transfer");
        if ((transfer.Status is StockTransferStatus.InTransit or StockTransferStatus.Received) && transfer.DispatchIdempotencyKey == idempotencyKey) { await transaction.CommitAsync(ct); return MapTransfer(transfer, true); }
        if (transfer.Status != StockTransferStatus.Draft) throw CommerceErrors.Conflict("stock_transfer_state_invalid", "Only a draft transfer can be dispatched.");
        if (await db.StockTransfers.AnyAsync(x => x.Id != transfer.Id && x.DispatchIdempotencyKey == idempotencyKey, ct)) throw CommerceErrors.Conflict("idempotency_key_reused", "The idempotency key belongs to another dispatch.");
        if (await db.Warehouses.CountAsync(x => (x.Id == transfer.FromWarehouseId || x.Id == transfer.ToWarehouseId) && x.IsActive, ct) != 2) throw CommerceErrors.Conflict("warehouse_inactive", "Both transfer warehouses must remain active.");
        var variantIds = transfer.Lines.Select(x => x.VariantId).Distinct().Order().ToArray();
        await db.LockInventoryAsync(variantIds, ct);
        var inventory = await db.Inventory.Include(x => x.Variant).ThenInclude(x => x.Product).Where(x => variantIds.Contains(x.VariantId)).ToDictionaryAsync(x => x.VariantId, ct);
        var stocks = await db.WarehouseStocks.Where(x => x.WarehouseId == transfer.FromWarehouseId && variantIds.Contains(x.VariantId)).ToDictionaryAsync(x => x.VariantId, ct);
        foreach (var line in transfer.Lines)
        {
            if (!inventory.TryGetValue(line.VariantId, out var aggregate) || !stocks.TryGetValue(line.VariantId, out var stock)) throw CommerceErrors.NotFound("Source warehouse stock");
            var source = line.LotId is null
                ? await InventoryBalanceOperations.EnsureUnscopedAvailableAsync(db, stock, clock.GetUtcNow(), ct)
                : await GetAvailableBalanceAsync(transfer.FromWarehouseId, line.VariantId, line.LotId, ct);
            if (source.Quantity < line.Quantity || OperationsCalculations.AvailableToSell(stock.OnHand, stock.Reserved, stock.SafetyStock, stock.Unavailable) < line.Quantity || aggregate.QuantityOnHand < line.Quantity) throw CommerceErrors.Conflict("insufficient_available_stock", "Source warehouse does not have enough available stock for the transfer.");
            source.Quantity -= line.Quantity; source.UpdatedAt = clock.GetUtcNow();
            await AddBalanceAsync(transfer.ToWarehouseId, line.VariantId, line.LotId, InventoryState.InTransit, line.Quantity, ct);
            stock.OnHand -= line.Quantity; stock.UpdatedAt = clock.GetUtcNow(); aggregate.QuantityOnHand -= line.Quantity; aggregate.UpdatedAt = clock.GetUtcNow();
            AddTransferLedger(transfer, line, transfer.FromWarehouseId, InventoryState.Available, -line.Quantity, InventoryMovementReason.TransferOut, actor);
            AddTransferLedger(transfer, line, transfer.ToWarehouseId, InventoryState.InTransit, line.Quantity, InventoryMovementReason.TransferIn, actor);
        }
        transfer.Status = StockTransferStatus.InTransit; transfer.DispatchIdempotencyKey = idempotencyKey; transfer.DispatchedBy = actor; transfer.DispatchedAt = clock.GetUtcNow();
        var lineIds = transfer.Lines.Select(x => x.Id.ToString()).ToArray();
        var dispatchTasks = await db.WarehouseTasks.Where(x => x.ReferenceType == "StockTransferDispatch" && lineIds.Contains(x.ReferenceId)).ToListAsync(ct);
        foreach (var task in dispatchTasks) { task.Status = WarehouseTaskStatus.Completed; task.AssignedTo = actor; task.CompletedQuantity = task.Quantity; task.CompletedAt = clock.GetUtcNow(); }
        foreach (var line in transfer.Lines)
            db.WarehouseTasks.Add(new WarehouseTask { Id = Guid.CreateVersion7(), Number = $"WT-{clock.GetUtcNow():yyyyMMdd}-{Guid.NewGuid():N}"[..24], WarehouseId = transfer.ToWarehouseId, Type = WarehouseTaskType.Putaway, Status = WarehouseTaskStatus.Open, Priority = 60, VariantId = line.VariantId, LotId = line.LotId, InventoryState = InventoryState.InTransit, Quantity = line.Quantity, ReferenceType = "StockTransferReceive", ReferenceId = line.Id.ToString(), CreatedBy = actor, CreatedAt = clock.GetUtcNow() });
        Audit("STOCK_TRANSFER_DISPATCHED", "StockTransfer", transfer.Id, actor, transfer.Number); await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); await InvalidateAsync(inventory.Values); return MapTransfer(transfer);
    }

    public async Task<StockTransferDto> ReceiveStockTransferAsync(Guid id, string idempotencyKey, string actor, CancellationToken ct)
    {
        ValidateIdempotencyKey(idempotencyKey);
        await using var transaction = await db.BeginTransactionAsync(ct);
        await db.LockStockTransferIdempotencyAsync($"receive:{idempotencyKey}", ct);
        await db.LockStockTransferAsync(id, ct);
        var transfer = await db.StockTransfers.Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw CommerceErrors.NotFound("Stock transfer");
        if (transfer.Status == StockTransferStatus.Received && transfer.ReceiveIdempotencyKey == idempotencyKey) { await transaction.CommitAsync(ct); return MapTransfer(transfer, true); }
        if (transfer.Status != StockTransferStatus.InTransit) throw CommerceErrors.Conflict("stock_transfer_state_invalid", "Only an in-transit transfer can be received.");
        if (await db.StockTransfers.AnyAsync(x => x.Id != transfer.Id && x.ReceiveIdempotencyKey == idempotencyKey, ct)) throw CommerceErrors.Conflict("idempotency_key_reused", "The idempotency key belongs to another receipt.");
        if (!await db.Warehouses.AnyAsync(x => x.Id == transfer.ToWarehouseId && x.IsActive, ct)) throw CommerceErrors.Conflict("warehouse_inactive", "The destination warehouse must remain active.");
        var variantIds = transfer.Lines.Select(x => x.VariantId).Distinct().Order().ToArray();
        await db.LockInventoryAsync(variantIds, ct);
        var inventory = await db.Inventory.Include(x => x.Variant).ThenInclude(x => x.Product).Where(x => variantIds.Contains(x.VariantId)).ToDictionaryAsync(x => x.VariantId, ct);
        var stocks = await db.WarehouseStocks.Where(x => x.WarehouseId == transfer.ToWarehouseId && variantIds.Contains(x.VariantId)).ToDictionaryAsync(x => x.VariantId, ct);
        foreach (var line in transfer.Lines)
        {
            var inbound = await db.InventoryBalances.SingleOrDefaultAsync(x => x.WarehouseId == transfer.ToWarehouseId && x.LocationId == null && x.VariantId == line.VariantId && x.LotId == line.LotId && x.State == InventoryState.InTransit, ct) ?? throw CommerceErrors.Conflict("transfer_balance_missing", "In-transit inventory is missing.");
            if (inbound.Quantity < line.Quantity) throw CommerceErrors.Conflict("transfer_balance_missing", "In-transit inventory is insufficient.");
            inbound.Quantity -= line.Quantity; inbound.UpdatedAt = clock.GetUtcNow(); await AddBalanceAsync(transfer.ToWarehouseId, line.VariantId, line.LotId, InventoryState.Available, line.Quantity, ct);
            if (!inventory.TryGetValue(line.VariantId, out var aggregate)) throw CommerceErrors.NotFound("Inventory");
            if (!stocks.TryGetValue(line.VariantId, out var stock)) { stock = new WarehouseStock { WarehouseId = transfer.ToWarehouseId, VariantId = line.VariantId, UpdatedAt = clock.GetUtcNow() }; db.WarehouseStocks.Add(stock); stocks[line.VariantId] = stock; }
            stock.OnHand += line.Quantity; stock.UpdatedAt = clock.GetUtcNow(); aggregate.QuantityOnHand += line.Quantity; aggregate.UpdatedAt = clock.GetUtcNow();
            AddTransferLedger(transfer, line, transfer.ToWarehouseId, InventoryState.InTransit, -line.Quantity, InventoryMovementReason.TransferIn, actor);
            AddTransferLedger(transfer, line, transfer.ToWarehouseId, InventoryState.Available, line.Quantity, InventoryMovementReason.TransferIn, actor);
        }
        transfer.Status = StockTransferStatus.Received; transfer.ReceiveIdempotencyKey = idempotencyKey; transfer.ReceivedBy = actor; transfer.ReceivedAt = clock.GetUtcNow();
        var lineIds = transfer.Lines.Select(x => x.Id.ToString()).ToArray();
        var tasks = await db.WarehouseTasks.Where(x => x.ReferenceType == "StockTransferReceive" && lineIds.Contains(x.ReferenceId)).ToListAsync(ct);
        foreach (var task in tasks) { task.Status = WarehouseTaskStatus.Completed; task.AssignedTo = actor; task.CompletedQuantity = task.Quantity; task.CompletedAt = clock.GetUtcNow(); }
        Audit("STOCK_TRANSFER_RECEIVED", "StockTransfer", transfer.Id, actor, transfer.Number); await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); await InvalidateAsync(inventory.Values); return MapTransfer(transfer);
    }

    public async Task<CycleCountDto> CreateCycleCountAsync(CreateCycleCountRequest request, string actor, CancellationToken ct)
    {
        if (request.Lines.Count is < 1 or > 500) throw CommerceErrors.Validation("A cycle count requires 1-500 lines.");
        var parsed = new List<(CycleCountScopeRequest Request, InventoryState State)>();
        foreach (var line in request.Lines)
        {
            if (!Enum.TryParse<InventoryState>(line.State, true, out var state)) throw CommerceErrors.Validation("A cycle-count inventory state is invalid.");
            parsed.Add((line, state));
        }
        if (parsed.Select(x => new { x.Request.VariantId, x.Request.LocationId, x.Request.LotId, x.State }).Distinct().Count() != parsed.Count) throw CommerceErrors.Validation("Cycle-count scope lines must be unique.");
        if (!await db.Warehouses.AnyAsync(x => x.Id == request.WarehouseId && x.IsActive, ct)) throw CommerceErrors.NotFound("Warehouse");
        var variantIds = parsed.Select(x => x.Request.VariantId).Distinct().ToArray();
        if (await db.ProductVariants.CountAsync(x => variantIds.Contains(x.Id), ct) != variantIds.Length) throw CommerceErrors.Validation("Every count line must reference a variant.");
        var locationIds = parsed.Where(x => x.Request.LocationId is not null).Select(x => x.Request.LocationId!.Value).Distinct().ToArray();
        if (locationIds.Length > 0 && await db.InventoryLocations.CountAsync(x => locationIds.Contains(x.Id) && x.WarehouseId == request.WarehouseId, ct) != locationIds.Length) throw CommerceErrors.Validation("Every count location must belong to the warehouse.");
        var lotIds = parsed.Where(x => x.Request.LotId is not null).Select(x => x.Request.LotId!.Value).Distinct().ToArray();
        var lots = lotIds.Length == 0 ? new Dictionary<Guid, Guid>() : await db.InventoryLots.Where(x => lotIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.VariantId, ct);
        if (parsed.Any(x => x.Request.LotId is not null && (!lots.TryGetValue(x.Request.LotId.Value, out var variantId) || variantId != x.Request.VariantId))) throw CommerceErrors.Validation("Every count lot must belong to its variant.");
        await using var transaction = await db.BeginTransactionAsync(ct);
        await db.LockReceiptIdempotencyAsync($"cycle-count-plan:{request.WarehouseId:N}", ct);
        await db.LockInventoryAsync(variantIds.Order().ToArray(), ct);
        foreach (var item in parsed)
        {
            var duplicate = await db.CycleCountLines.AnyAsync(x => x.CycleCount.WarehouseId == request.WarehouseId && x.VariantId == item.Request.VariantId && x.LocationId == item.Request.LocationId && x.LotId == item.Request.LotId && x.State == item.State && (x.CycleCount.Status == CycleCountStatus.Planned || x.CycleCount.Status == CycleCountStatus.InProgress || x.CycleCount.Status == CycleCountStatus.Submitted), ct);
            if (duplicate) throw CommerceErrors.Conflict("cycle_count_scope_active", "An active cycle count already covers this exact inventory scope.");
        }
        var now = clock.GetUtcNow();
        var count = new CycleCount { Id = Guid.CreateVersion7(), Number = $"CC-{now:yyyyMMdd}-{Guid.NewGuid():N}"[..24], WarehouseId = request.WarehouseId, Status = CycleCountStatus.Planned, CreatedBy = actor, CreatedAt = now };
        foreach (var item in parsed)
        {
            var expected = await ExpectedAsync(request.WarehouseId, item.Request, item.State, ct);
            var line = new CycleCountLine { Id = Guid.CreateVersion7(), VariantId = item.Request.VariantId, LocationId = item.Request.LocationId, LotId = item.Request.LotId, State = item.State, ExpectedQuantity = expected };
            count.Lines.Add(line);
            db.WarehouseTasks.Add(new WarehouseTask { Id = Guid.CreateVersion7(), Number = $"WT-{now:yyyyMMdd}-{Guid.NewGuid():N}"[..24], WarehouseId = request.WarehouseId, Type = WarehouseTaskType.CycleCount, Status = WarehouseTaskStatus.Open, Priority = 50, SourceLocationId = item.Request.LocationId, VariantId = item.Request.VariantId, LotId = item.Request.LotId, InventoryState = item.State, Quantity = 1, ReferenceType = "CycleCountLine", ReferenceId = line.Id.ToString(), CreatedBy = actor, CreatedAt = now });
        }
        db.CycleCounts.Add(count); Audit("CYCLE_COUNT_PLANNED", "CycleCount", count.Id, actor, count.Number); await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); return MapCount(count);
    }

    public async Task<CycleCountDto> StartCycleCountAsync(Guid id, string actor, CancellationToken ct)
    {
        await using var transaction = await db.BeginTransactionAsync(ct);
        await db.LockCycleCountAsync(id, ct);
        var count = await db.CycleCounts.Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw CommerceErrors.NotFound("Cycle count");
        if (count.Status != CycleCountStatus.Planned) throw CommerceErrors.Conflict("cycle_count_state_invalid", "Only a planned count can start.");
        count.Status = CycleCountStatus.InProgress; count.CountedBy = actor;
        var lineIds = count.Lines.Select(x => x.Id.ToString()).ToArray();
        var tasks = await db.WarehouseTasks.Where(x => x.ReferenceType == "CycleCountLine" && lineIds.Contains(x.ReferenceId)).ToListAsync(ct);
        foreach (var task in tasks) { task.Status = WarehouseTaskStatus.InProgress; task.AssignedTo = actor; }
        Audit("CYCLE_COUNT_STARTED", "CycleCount", count.Id, actor, count.Number); await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); return MapCount(count);
    }

    public async Task<CycleCountDto> SubmitCycleCountAsync(Guid id, SubmitCycleCountRequest request, string actor, CancellationToken ct)
    {
        if (request.Lines.Count is < 1 or > 500 || request.Lines.Select(x => x.LineId).Distinct().Count() != request.Lines.Count || request.Lines.Any(x => x.CountedQuantity < 0)) throw CommerceErrors.Validation("Counted quantities are invalid.");
        await using var transaction = await db.BeginTransactionAsync(ct);
        await db.LockCycleCountAsync(id, ct);
        var count = await db.CycleCounts.Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw CommerceErrors.NotFound("Cycle count");
        if (count.Status != CycleCountStatus.InProgress) throw CommerceErrors.Conflict("cycle_count_state_invalid", "Only an in-progress count can be submitted.");
        if (count.CountedBy != actor) throw new CommerceException("cycle_count_assignment_required", "Only the assigned counter can submit this count.", 403);
        if (request.Lines.Count != count.Lines.Count || request.Lines.Any(x => count.Lines.All(l => l.Id != x.LineId))) throw CommerceErrors.Validation("A submitted count must contain every planned line exactly once.");
        foreach (var input in request.Lines) count.Lines.Single(x => x.Id == input.LineId).CountedQuantity = input.CountedQuantity;
        count.Status = CycleCountStatus.Submitted; count.SubmittedAt = clock.GetUtcNow(); Audit("CYCLE_COUNT_SUBMITTED", "CycleCount", count.Id, actor, count.Number); await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); return MapCount(count);
    }

    public async Task<CycleCountDto> ReconcileCycleCountAsync(Guid id, string actor, CancellationToken ct)
    {
        await using var transaction = await db.BeginTransactionAsync(ct);
        await db.LockCycleCountAsync(id, ct);
        var count = await db.CycleCounts.Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw CommerceErrors.NotFound("Cycle count");
        if (count.Status != CycleCountStatus.Submitted || count.Lines.Any(x => x.CountedQuantity is null)) throw CommerceErrors.Conflict("cycle_count_state_invalid", "Only a complete submitted count can be reconciled.");
        if (count.CountedBy == actor) throw new CommerceException("maker_checker_required", "The counter cannot reconcile the same cycle count.", 403);
        var variantIds = count.Lines.Select(x => x.VariantId).Distinct().Order().ToArray();
        await db.LockInventoryAsync(variantIds, ct);
        var inventory = await db.Inventory.Include(x => x.Variant).ThenInclude(x => x.Product).Where(x => variantIds.Contains(x.VariantId)).ToDictionaryAsync(x => x.VariantId, ct);
        var stocks = await db.WarehouseStocks.Where(x => x.WarehouseId == count.WarehouseId && variantIds.Contains(x.VariantId)).ToDictionaryAsync(x => x.VariantId, ct);
        foreach (var line in count.Lines)
        {
            var counted = line.CountedQuantity!.Value;
            var balance = await db.InventoryBalances.SingleOrDefaultAsync(x => x.WarehouseId == count.WarehouseId && x.LocationId == line.LocationId && x.VariantId == line.VariantId && x.LotId == line.LotId && x.State == line.State, ct);
            if (balance is null || balance.Quantity != line.ExpectedQuantity) throw CommerceErrors.Conflict("cycle_count_scope_changed", "Inventory moved after this count was planned. Cancel it and create a new count.");
            var delta = counted - balance.Quantity;
            balance.Quantity = counted; balance.UpdatedAt = clock.GetUtcNow();
            if (line.State == InventoryState.Available && delta != 0)
            {
                if (!inventory.TryGetValue(line.VariantId, out var aggregate)) throw CommerceErrors.NotFound("Inventory");
                if (!stocks.TryGetValue(line.VariantId, out var stock)) { stock = new WarehouseStock { WarehouseId = count.WarehouseId, VariantId = line.VariantId, UpdatedAt = clock.GetUtcNow() }; db.WarehouseStocks.Add(stock); stocks[line.VariantId] = stock; }
                if (aggregate.QuantityOnHand + delta < 0 || stock.OnHand + delta < stock.Reserved + stock.Unavailable) throw CommerceErrors.Conflict("cycle_count_inventory_conflict", "The count would reduce available inventory below committed stock.");
                aggregate.QuantityOnHand += delta; aggregate.UpdatedAt = clock.GetUtcNow(); stock.OnHand += delta; stock.UpdatedAt = clock.GetUtcNow();
            }
            if (delta != 0) db.InventoryLedgerEntries.Add(new InventoryLedgerEntry { Id = Guid.CreateVersion7(), VariantId = line.VariantId, WarehouseId = count.WarehouseId, LocationId = line.LocationId, LotId = line.LotId, QuantityDelta = delta, State = line.State, Reason = InventoryMovementReason.CycleCount, ReferenceType = "CycleCount", ReferenceId = count.Id.ToString(), CreatedBy = actor, CreatedAt = clock.GetUtcNow() });
        }
        count.Status = CycleCountStatus.Reconciled; count.ReconciledBy = actor; count.ReconciledAt = clock.GetUtcNow();
        var lineIds = count.Lines.Select(x => x.Id.ToString()).ToArray();
        var tasks = await db.WarehouseTasks.Where(x => x.ReferenceType == "CycleCountLine" && lineIds.Contains(x.ReferenceId)).ToListAsync(ct);
        foreach (var task in tasks) { task.Status = WarehouseTaskStatus.Completed; task.CompletedQuantity = 1; task.CompletedAt = clock.GetUtcNow(); }
        Audit("CYCLE_COUNT_RECONCILED", "CycleCount", count.Id, actor, count.Number); await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        foreach (var aggregate in inventory.Values) { await cache.RemoveByTagAsync($"inventory:{aggregate.VariantId}", CancellationToken.None); await cache.RemoveByTagAsync($"product:{aggregate.Variant.Product.Slug.ToLowerInvariant()}", CancellationToken.None); }
        await cache.RemoveByTagAsync("products", CancellationToken.None); await cache.RemoveByTagAsync("homepage", CancellationToken.None);
        return MapCount(count);
    }

    private async Task<int> ExpectedAsync(Guid warehouseId, CycleCountScopeRequest line, InventoryState state, CancellationToken ct)
    {
        var balance = await db.InventoryBalances.SingleOrDefaultAsync(x => x.WarehouseId == warehouseId && x.LocationId == line.LocationId && x.VariantId == line.VariantId && x.LotId == line.LotId && x.State == state, ct);
        if (balance is not null) return balance.Quantity;
        var anyBalances = await db.InventoryBalances.AnyAsync(x => x.WarehouseId == warehouseId && x.VariantId == line.VariantId, ct);
        var quantity = 0;
        if (!anyBalances && state == InventoryState.Available && line.LocationId is null && line.LotId is null) quantity = await db.WarehouseStocks.AsNoTracking().Where(x => x.WarehouseId == warehouseId && x.VariantId == line.VariantId).Select(x => (int?)x.OnHand).SingleOrDefaultAsync(ct) ?? 0;
        db.InventoryBalances.Add(new InventoryBalance { Id = Guid.CreateVersion7(), WarehouseId = warehouseId, LocationId = line.LocationId, VariantId = line.VariantId, LotId = line.LotId, State = state, Quantity = quantity, UpdatedAt = clock.GetUtcNow() });
        return quantity;
    }

    private async Task<InventoryBalance> GetAvailableBalanceAsync(Guid warehouseId, Guid variantId, Guid? lotId, CancellationToken ct)
    {
        var balance = await db.InventoryBalances.SingleOrDefaultAsync(x => x.WarehouseId == warehouseId && x.LocationId == null && x.VariantId == variantId && x.LotId == lotId && x.State == InventoryState.Available, ct);
        if (balance is not null) return balance;
        throw CommerceErrors.Conflict("insufficient_available_stock", "No available balance exists for the requested lot.");
    }

    private async Task AddBalanceAsync(Guid warehouseId, Guid variantId, Guid? lotId, InventoryState state, int delta, CancellationToken ct)
    {
        var balance = await db.InventoryBalances.SingleOrDefaultAsync(x => x.WarehouseId == warehouseId && x.LocationId == null && x.VariantId == variantId && x.LotId == lotId && x.State == state, ct);
        if (balance is null) db.InventoryBalances.Add(new InventoryBalance { Id = Guid.CreateVersion7(), WarehouseId = warehouseId, VariantId = variantId, LotId = lotId, State = state, Quantity = delta, UpdatedAt = clock.GetUtcNow() }); else { balance.Quantity += delta; balance.UpdatedAt = clock.GetUtcNow(); }
    }

    private void AddTransferLedger(StockTransfer transfer, StockTransferLine line, Guid warehouseId, InventoryState state, int delta, InventoryMovementReason reason, string actor) => db.InventoryLedgerEntries.Add(new InventoryLedgerEntry { Id = Guid.CreateVersion7(), VariantId = line.VariantId, WarehouseId = warehouseId, LotId = line.LotId, QuantityDelta = delta, State = state, Reason = reason, ReferenceType = "StockTransfer", ReferenceId = transfer.Id.ToString(), CreatedBy = actor, CreatedAt = clock.GetUtcNow() });

    private async Task InvalidateAsync(IEnumerable<InventoryItem> inventory)
    {
        foreach (var aggregate in inventory) { await cache.RemoveByTagAsync($"inventory:{aggregate.VariantId}", CancellationToken.None); await cache.RemoveByTagAsync($"product:{aggregate.Variant.Product.Slug.ToLowerInvariant()}", CancellationToken.None); }
        await cache.RemoveByTagAsync("products", CancellationToken.None); await cache.RemoveByTagAsync("homepage", CancellationToken.None);
    }

    private static void ValidateIdempotencyKey(string key) { if (string.IsNullOrWhiteSpace(key) || key.Length > 128) throw CommerceErrors.Validation("A valid idempotency key is required."); }

    private static CycleCountDto MapCount(CycleCount x)
    {
        var revealExpected = x.Status is CycleCountStatus.Submitted or CycleCountStatus.Reconciled;
        return new(x.Id, x.Number, x.WarehouseId, x.Status.ToString(), x.CreatedBy, x.CreatedAt, x.CountedBy, x.ReconciledBy, x.Lines.Select(l => new CycleCountLineDto(l.Id, l.VariantId, l.LocationId, l.LotId, l.State.ToString(), revealExpected ? l.ExpectedQuantity : null, l.CountedQuantity, revealExpected && l.CountedQuantity is not null ? l.CountedQuantity.Value - l.ExpectedQuantity : null)).ToList());
    }

    private static StockTransferDto MapTransfer(StockTransfer x, bool replayed = false) => new(x.Id, x.Number, x.FromWarehouseId, x.ToWarehouseId, x.Status.ToString(), x.CreatedBy, x.CreatedAt, x.DispatchedBy, x.DispatchedAt, x.ReceivedBy, x.ReceivedAt, x.Lines.Select(l => new StockTransferLineDto(l.Id, l.VariantId, l.LotId, l.Quantity)).ToList(), replayed);

    private void Audit(string eventType, string resourceType, Guid resourceId, string actor, string reason) => db.OperationsAuditEntries.Add(new OperationsAuditEntry { Id = Guid.CreateVersion7(), EventType = eventType, ResourceType = resourceType, ResourceId = resourceId.ToString(), ActorId = actor, Reason = reason[..Math.Min(reason.Length, 500)], CreatedAt = clock.GetUtcNow() });
}
