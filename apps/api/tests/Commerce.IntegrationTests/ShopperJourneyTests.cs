using System.Net;
using System.Net.Http.Json;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Commerce.Contracts;
using Commerce.Application;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Testcontainers.PostgreSql;
using Xunit;

namespace Commerce.IntegrationTests;

public sealed class ShopperJourneyTests
{
    private const string TestIssuer = "https://identity.test/realms/commerce";
    private const string TestAudience = "commerce-api";
    private static readonly RSA SigningRsa = RSA.Create(2048);
    private static readonly RsaSecurityKey SigningKey = new(SigningRsa) { KeyId = "integration-test-key" };
    private static readonly RsaSecurityKey OtherSigningKey = new(RSA.Create(2048)) { KeyId = "wrong-key" };

    [Fact]
    public async Task Shopper_can_browse_mutate_cart_checkout_and_replay_idempotently()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync();
        await using var factory = CreateFactory(postgres.GetConnectionString(), cacheEnabled: false);
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/cart")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/catalog/products?pageSize=101")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/storefront/products/not-a-product")).StatusCode);
        using var malformed = new HttpRequestMessage(HttpMethod.Put, "/api/cart/items") { Content = new StringContent("{not-json", System.Text.Encoding.UTF8, "application/json") };
        malformed.Headers.Add("X-Customer-Id", "shopper-1");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(malformed)).StatusCode);

        var categoriesResponse = await client.GetAsync("/api/catalog/categories");
        categoriesResponse.EnsureSuccessStatusCode();
        var categoriesEtag = Assert.Single(categoriesResponse.Headers.ETag is null ? [] : new[] { categoriesResponse.Headers.ETag.Tag });
        using var conditionalCategories = new HttpRequestMessage(HttpMethod.Get, "/api/catalog/categories");
        conditionalCategories.Headers.TryAddWithoutValidation("If-None-Match", categoriesEtag);
        Assert.Equal(HttpStatusCode.NotModified, (await client.SendAsync(conditionalCategories)).StatusCode);

        var search = await client.GetFromJsonAsync<ProductPageDto>("/api/catalog/products?q=keyboard&category=electronics&sort=price-asc&pageSize=5");
        Assert.Single(search!.Items);
        Assert.Equal("Mechanical Keyboard", search.Items[0].Title);

        var home = await client.GetFromJsonAsync<HomeDto>("/api/storefront/home");
        Assert.NotNull(home);
        var card = Assert.Single(home.Rails, rail => rail.Id == "featured").Products[0];
        var product = await client.GetFromJsonAsync<StorefrontProductDto>($"/api/storefront/products/{card.Slug}");
        Assert.NotNull(product);
        var variant = product.Product.Variants[0];

        using var unauthorizedAdmin = await client.GetAsync($"/api/admin/products/{card.Id}");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorizedAdmin.StatusCode);
        using var adminClient = factory.CreateClient();
        adminClient.DefaultRequestHeaders.Add("X-Admin", "true");
        var adminProductResponse = await adminClient.GetAsync($"/api/admin/products/{card.Id}");
        adminProductResponse.EnsureSuccessStatusCode();
        var adminProduct = await adminProductResponse.Content.ReadFromJsonAsync<AdminProductDto>();
        var productEtag = adminProductResponse.Headers.ETag!.Tag;
        var update = new UpdateProductRequest($"{adminProduct!.Title} Updated", adminProduct.Brand, adminProduct.Description, adminProduct.IsFeatured, adminProduct.Status);
        using var updateRequest = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/products/{card.Id}") { Content = JsonContent.Create(update) };
        updateRequest.Headers.TryAddWithoutValidation("If-Match", productEtag);
        var updateResponse = await adminClient.SendAsync(updateRequest);
        updateResponse.EnsureSuccessStatusCode();
        using var staleUpdate = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/products/{card.Id}") { Content = JsonContent.Create(update) };
        staleUpdate.Headers.TryAddWithoutValidation("If-Match", productEtag);
        Assert.Equal(HttpStatusCode.Conflict, (await adminClient.SendAsync(staleUpdate)).StatusCode);
        var refreshedProduct = await client.GetFromJsonAsync<StorefrontProductDto>($"/api/storefront/products/{card.Slug}");
        Assert.EndsWith(" Updated", refreshedProduct!.Product.Title);

        var inventoryResponse = await adminClient.GetAsync($"/api/admin/inventory/{variant.Id}");
        inventoryResponse.EnsureSuccessStatusCode();
        using var inventoryUpdate = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/inventory/{variant.Id}") { Content = JsonContent.Create(new UpdateInventoryRequest(10)) };
        inventoryUpdate.Headers.TryAddWithoutValidation("If-Match", inventoryResponse.Headers.ETag!.Tag);
        var updatedInventoryResponse = await adminClient.SendAsync(inventoryUpdate);
        updatedInventoryResponse.EnsureSuccessStatusCode();
        var updatedInventory = await updatedInventoryResponse.Content.ReadFromJsonAsync<InventoryDto>();
        Assert.Equal(10, updatedInventory!.QuantityOnHand);

        client.DefaultRequestHeaders.Add("X-Customer-Id", "shopper-1");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/api/cart/items", new SetCartItemRequest(variant.Id, 0))).StatusCode);
        var cartResponse = await client.PutAsJsonAsync("/api/cart/items", new SetCartItemRequest(variant.Id, 2));
        cartResponse.EnsureSuccessStatusCode();
        var cart = await cartResponse.Content.ReadFromJsonAsync<CartMutationDto>();
        Assert.Equal(2, cart!.TotalQuantity);
        Assert.Equal(variant.Price.Amount * 2, cart.Subtotal.Amount);

        var authoritativePrice = variant.Price.Amount + 5m;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Commerce.Infrastructure.CommerceDbContext>();
            var trackedVariant = await db.ProductVariants.SingleAsync(x => x.Id == variant.Id);
            trackedVariant.Price = authoritativePrice;
            await db.SaveChangesAsync();
        }

        const string idempotencyKey = "integration-checkout-1";
        var checkoutRequest = new CheckoutRequest(new AddressRequest("Ada Shopper", "1 Market Street", null, "Seattle", "WA", "98101", "US"));
        using var firstRequest = new HttpRequestMessage(HttpMethod.Post, "/api/checkout/confirm") { Content = JsonContent.Create(checkoutRequest) };
        firstRequest.Headers.Add("Idempotency-Key", idempotencyKey);
        var firstResponse = await client.SendAsync(firstRequest);
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        var firstCheckout = await firstResponse.Content.ReadFromJsonAsync<CheckoutResultDto>();
        Assert.False(firstCheckout!.IdempotencyReplayed);
        Assert.Equal(authoritativePrice * 2, firstCheckout.Order.Subtotal.Amount);

        using var replayRequest = new HttpRequestMessage(HttpMethod.Post, "/api/checkout/confirm") { Content = JsonContent.Create(checkoutRequest) };
        replayRequest.Headers.Add("Idempotency-Key", idempotencyKey);
        var replayResponse = await client.SendAsync(replayRequest);
        Assert.Equal(HttpStatusCode.Created, replayResponse.StatusCode);
        Assert.Equal("true", replayResponse.Headers.GetValues("Idempotency-Replayed").Single());
        var replay = await replayResponse.Content.ReadFromJsonAsync<CheckoutResultDto>();
        Assert.Equal(firstCheckout.Order.Id, replay!.Order.Id);

        var changedCheckout = checkoutRequest with { ShippingAddress = checkoutRequest.ShippingAddress with { City = "Portland" } };
        using var conflictRequest = new HttpRequestMessage(HttpMethod.Post, "/api/checkout/confirm") { Content = JsonContent.Create(changedCheckout) };
        conflictRequest.Headers.Add("Idempotency-Key", idempotencyKey);
        Assert.Equal(HttpStatusCode.Conflict, (await client.SendAsync(conflictRequest)).StatusCode);

        var finalCart = await client.GetFromJsonAsync<CartDto>("/api/cart");
        Assert.Equal(0, finalCart!.TotalQuantity);
        var orders = await client.GetFromJsonAsync<IReadOnlyList<OrderDto>>("/api/orders");
        Assert.Single(orders!);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Commerce.Infrastructure.CommerceDbContext>();
            var stock = await db.WarehouseStocks.SingleAsync(x => x.VariantId == variant.Id);
            Assert.Equal(stock.OnHand, await db.InventoryBalances.Where(x => x.WarehouseId == stock.WarehouseId && x.VariantId == variant.Id && x.State == Commerce.Domain.InventoryState.Available).SumAsync(x => x.Quantity));
            Assert.Equal(-2, await db.InventoryLedgerEntries.Where(x => x.ReferenceType == "Order" && x.ReferenceId == firstCheckout.Order.Id.ToString() && x.State == Commerce.Domain.InventoryState.Available).SumAsync(x => x.QuantityDelta));
        }
    }

    [Fact]
    public async Task Jwt_validation_rejects_expired_wrong_issuer_audience_and_signature()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync();
        await using var factory = CreateFactory(postgres.GetConnectionString(), cacheEnabled: false);

        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/cart")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await BearerClient(factory, Token("valid-customer")).GetAsync("/api/cart")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await BearerClient(factory, Token("expired", expires: DateTime.UtcNow.AddMinutes(-2))).GetAsync("/api/cart")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await BearerClient(factory, Token("wrong-issuer", issuer: "https://wrong.example/realms/commerce")).GetAsync("/api/cart")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await BearerClient(factory, Token("wrong-audience", audience: "other-api")).GetAsync("/api/cart")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await BearerClient(factory, Token("wrong-signature", signingKey: OtherSigningKey)).GetAsync("/api/cart")).StatusCode);
    }

    [Fact]
    public async Task Order_access_enforces_ownership_and_explicit_read_any_permission()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync();
        await using var factory = CreateFactory(postgres.GetConnectionString(), cacheEnabled: false);
        using var owner = BearerClient(factory, Token("order-owner"));
        var product = await owner.GetFromJsonAsync<StorefrontProductDto>("/api/storefront/products/noise-cancelling-headphones");
        var variantId = product!.Product.Variants[0].Id;
        (await owner.PutAsJsonAsync("/api/cart/items", new SetCartItemRequest(variantId, 1))).EnsureSuccessStatusCode();
        using var checkout = new HttpRequestMessage(HttpMethod.Post, "/api/checkout/confirm") { Content = JsonContent.Create(new CheckoutRequest(new AddressRequest("Order Owner", "1 Main Street", null, "Austin", "TX", "78701", "US"))) };
        checkout.Headers.Add("Idempotency-Key", "jwt-owner-order");
        var checkoutResponse = await owner.SendAsync(checkout);
        checkoutResponse.EnsureSuccessStatusCode();
        var order = (await checkoutResponse.Content.ReadFromJsonAsync<CheckoutResultDto>())!.Order;

        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/api/orders/{order.Id}")).StatusCode);
        using var forgedIdentity = BearerClient(factory, Token("order-owner"));
        forgedIdentity.DefaultRequestHeaders.Add("X-Customer-Id", "other-customer");
        Assert.Equal(HttpStatusCode.OK, (await forgedIdentity.GetAsync($"/api/orders/{order.Id}")).StatusCode);
        using var otherCustomer = BearerClient(factory, Token("other-customer"));
        Assert.Equal(HttpStatusCode.NotFound, (await otherCustomer.GetAsync($"/api/orders/{order.Id}")).StatusCode);
        using var support = BearerClient(factory, Token("support-user", ["orders-view-any"]));
        Assert.Equal(HttpStatusCode.OK, (await support.GetAsync($"/api/orders/{order.Id}")).StatusCode);
    }

    [Fact]
    public async Task Keycloak_client_roles_are_normalized_into_named_admin_policies()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync();
        await using var factory = CreateFactory(postgres.GetConnectionString(), cacheEnabled: false);
        using var anonymous = factory.CreateClient();
        var home = await anonymous.GetFromJsonAsync<HomeDto>("/api/storefront/home");
        var productId = home!.Rails.SelectMany(x => x.Products).First().Id;

        using var customer = BearerClient(factory, Token("normal-customer"));
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.GetAsync($"/api/admin/products/{productId}")).StatusCode);

        using var viewer = BearerClient(factory, Token("catalog-viewer", ["administration-access", "catalog-view"]));
        var viewerGet = await viewer.GetAsync($"/api/admin/products/{productId}");
        viewerGet.EnsureSuccessStatusCode();
        var current = (await viewerGet.Content.ReadFromJsonAsync<AdminProductDto>())!;
        using var forbiddenUpdate = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/products/{productId}") { Content = JsonContent.Create(new UpdateProductRequest(current.Title, current.Brand, current.Description, current.IsFeatured, current.Status)) };
        forbiddenUpdate.Headers.TryAddWithoutValidation("If-Match", viewerGet.Headers.ETag!.Tag);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.SendAsync(forbiddenUpdate)).StatusCode);

        using var manager = BearerClient(factory, Token("catalog-manager", ["administration-access", "catalog-view", "catalog-manage"]));
        var managerGet = await manager.GetAsync($"/api/admin/products/{productId}");
        managerGet.EnsureSuccessStatusCode();
        var managed = (await managerGet.Content.ReadFromJsonAsync<AdminProductDto>())!;
        using var allowedUpdate = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/products/{productId}") { Content = JsonContent.Create(new UpdateProductRequest(managed.Title, managed.Brand, managed.Description, managed.IsFeatured, managed.Status)) };
        allowedUpdate.Headers.TryAddWithoutValidation("If-Match", managerGet.Headers.ETag!.Tag);
        Assert.Equal(HttpStatusCode.OK, (await manager.SendAsync(allowedUpdate)).StatusCode);

        using var malformed = BearerClient(factory, Token("malformed-roles", malformedRoles: true));
        Assert.Equal(HttpStatusCode.Forbidden, (await malformed.GetAsync($"/api/admin/products/{productId}")).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync("/api/admin/operations/variants")).StatusCode);
        using var pricingViewer = BearerClient(factory, Token("pricing-viewer", ["administration-access", "pricing-view"]));
        Assert.Equal(HttpStatusCode.OK, (await pricingViewer.GetAsync("/api/admin/operations/variants")).StatusCode);

        var card = home.Rails.SelectMany(x => x.Products).First();
        using var pricingManager = BearerClient(factory, Token("pricing-manager", ["administration-access", "pricing-view", "pricing-manage", "pricing-approve"]));
        var proposedPrice = decimal.Round(card.Price.Amount * 1.2m, 2);
        var schedule = new PriceScheduleRequest(card.DefaultVariantId, proposedPrice, null, DateTimeOffset.UtcNow.AddDays(1), null, "Regular", "Integration test governed price change");
        var scheduleResponse = await pricingManager.PostAsJsonAsync("/api/admin/operations/pricing/schedules", schedule);
        Assert.Equal(HttpStatusCode.Created, scheduleResponse.StatusCode);
        var priceRecord = await scheduleResponse.Content.ReadFromJsonAsync<PriceRecordDto>();
        Assert.Equal("ReviewRequired", priceRecord!.Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await pricingManager.PostAsJsonAsync($"/api/admin/operations/pricing/{priceRecord.Id}/approve", new PriceApprovalRequest("Self approval must fail"))).StatusCode);

        using var pricingApprover = BearerClient(factory, Token("pricing-approver", ["administration-access", "pricing-view", "pricing-approve"]));
        var approvalResponse = await pricingApprover.PostAsJsonAsync($"/api/admin/operations/pricing/{priceRecord.Id}/approve", new PriceApprovalRequest("Independent approval"));
        approvalResponse.EnsureSuccessStatusCode();
        Assert.Equal("Scheduled", (await approvalResponse.Content.ReadFromJsonAsync<PriceRecordDto>())!.Status);

        var activationCard = home.Rails.SelectMany(x => x.Products).DistinctBy(x => x.DefaultVariantId).Skip(1).First();
        var activationPrice = decimal.Round(activationCard.Price.Amount * 1.05m, 2);
        var activationResponse = await pricingManager.PostAsJsonAsync("/api/admin/operations/pricing/schedules", new PriceScheduleRequest(activationCard.DefaultVariantId, activationPrice, null, DateTimeOffset.UtcNow.AddDays(2), null, "Regular", "Scheduled activation test"));
        activationResponse.EnsureSuccessStatusCode();
        var activationRecord = (await activationResponse.Content.ReadFromJsonAsync<PriceRecordDto>())!;
        Assert.Equal("Scheduled", activationRecord.Status);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Commerce.Infrastructure.CommerceDbContext>();
            var tracked = await db.PriceRecords.SingleAsync(x => x.Id == activationRecord.Id);
            tracked.EffectiveFrom = DateTimeOffset.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
            await scope.ServiceProvider.GetRequiredService<OperationsService>().ApplyDuePricesAsync("integration-test", CancellationToken.None);
            db.ChangeTracker.Clear();
            Assert.Equal(activationPrice, await db.ProductVariants.Where(x => x.Id == activationCard.DefaultVariantId).Select(x => x.Price).SingleAsync());
        }
    }

    [Fact]
    public async Task Physical_receipt_posts_only_disposition_quantities_and_is_idempotent()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync();
        await using var factory = CreateFactory(postgres.GetConnectionString(), cacheEnabled: false);
        Guid warehouseId;
        Guid variantId;
        int initialInventory;
        int initialWarehouseStock;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Commerce.Infrastructure.CommerceDbContext>();
            warehouseId = await db.Warehouses.Select(x => x.Id).FirstAsync();
            variantId = await db.ProductVariants.Select(x => x.Id).FirstAsync();
            initialInventory = await db.Inventory.Where(x => x.VariantId == variantId).Select(x => x.QuantityOnHand).SingleAsync();
            initialWarehouseStock = await db.WarehouseStocks.Where(x => x.WarehouseId == warehouseId && x.VariantId == variantId).Select(x => x.OnHand).SingleAsync();
        }

        using var buyer = BearerClient(factory, Token("supply-chain-buyer", ["administration-access", "suppliers-view", "suppliers-manage", "procurement-view", "procurement-manage", "invoices-manage"]));
        using var approver = BearerClient(factory, Token("supply-chain-approver", ["administration-access", "procurement-view", "procurement-approve"]));
        using var receiver = BearerClient(factory, Token("warehouse-receiver", ["administration-access", "receiving-view", "receiving-manage", "invoices-match"]));
        using var counter = BearerClient(factory, Token("warehouse-counter", ["administration-access", "cycle-counts-view", "cycle-counts-manage", "cycle-counts-reconcile", "warehouse-tasks-view"]));
        using var reconciler = BearerClient(factory, Token("warehouse-reconciler", ["administration-access", "cycle-counts-view", "cycle-counts-reconcile", "warehouse-tasks-view"]));
        using var transferOperator = BearerClient(factory, Token("transfer-operator", ["administration-access", "stock-transfers-view", "stock-transfers-manage", "warehouse-tasks-view"]));

        var supplierResponse = await buyer.PostAsJsonAsync("/api/admin/supply-chain/suppliers", new CreateSupplierRequest("SYNTH-1", "Synthetic Supplier", "US", "TAX-1"));
        supplierResponse.EnsureSuccessStatusCode();
        var supplier = (await supplierResponse.Content.ReadFromJsonAsync<SupplierDto>())!;
        var sourceResponse = await buyer.PostAsJsonAsync("/api/admin/supply-chain/sources", new SupplierSourceRequest(supplier.Id, variantId, "SUP-100", "USD", 10m, 7, 1, 1, 98m));
        sourceResponse.EnsureSuccessStatusCode();
        var source = (await sourceResponse.Content.ReadFromJsonAsync<SupplierSourceDto>())!;
        var orderResponse = await buyer.PostAsJsonAsync("/api/admin/supply-chain/purchase-orders", new CreatePurchaseOrderRequest(supplier.Id, warehouseId, DateTimeOffset.UtcNow.AddDays(7), [new PurchaseOrderLineRequest(source.Id, 100)]));
        orderResponse.EnsureSuccessStatusCode();
        var order = (await orderResponse.Content.ReadFromJsonAsync<PurchaseOrderDto>())!;
        Assert.Equal("Draft", order.Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await buyer.PostAsync($"/api/admin/supply-chain/purchase-orders/{order.Id}/approve", null)).StatusCode);
        var approvalResponse = await approver.PostAsync($"/api/admin/supply-chain/purchase-orders/{order.Id}/approve", null);
        approvalResponse.EnsureSuccessStatusCode();

        InboundShipmentDto inbound;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<SupplyChainService>();
            var request = new CreateInboundShipmentRequest(order.Id, "Synthetic Carrier", "TRACK-100", DateTimeOffset.UtcNow.AddDays(7), [new CreateInboundShipmentLineRequest(order.Lines[0].Id, 100)]);
            inbound = await service.CreateInboundShipmentAsync(request, supplier.Id, "synthetic-asn-100", "integration-supplier", CancellationToken.None);
            var asnReplay = await service.CreateInboundShipmentAsync(request, supplier.Id, "synthetic-asn-100", "integration-supplier", CancellationToken.None);
            Assert.True(asnReplay.IdempotencyReplayed);
            Assert.Equal(inbound.Id, asnReplay.Id);
        }

        var invoiceRequest = new InvoiceIngestionRequest(supplier.Id, order.Id, "GENERIC", "INV-SYNTH-100", "USD", 1000m, DateTimeOffset.UtcNow, "object://invoices/inv-synth-100.pdf", "invoice|INV-SYNTH-100|100", [new InvoiceLineRequest(order.Lines[0].Id, "SUP-100", 100, 10m)]);
        var invoiceResponse = await buyer.PostAsJsonAsync("/api/admin/supply-chain/invoices", invoiceRequest);
        invoiceResponse.EnsureSuccessStatusCode();
        var invoice = (await invoiceResponse.Content.ReadFromJsonAsync<FiscalInvoiceDto>())!;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Commerce.Infrastructure.CommerceDbContext>();
            Assert.Equal(initialInventory, await db.Inventory.Where(x => x.VariantId == variantId).Select(x => x.QuantityOnHand).SingleAsync());
            Assert.Equal(initialWarehouseStock, await db.WarehouseStocks.Where(x => x.WarehouseId == warehouseId && x.VariantId == variantId).Select(x => x.OnHand).SingleAsync());
            Assert.Equal(initialWarehouseStock, await db.InventoryBalances.Where(x => x.WarehouseId == warehouseId && x.VariantId == variantId && x.State == Commerce.Domain.InventoryState.Available).SumAsync(x => x.Quantity));
            Assert.Empty(await db.InventoryLedgerEntries.Where(x => x.ReferenceType == "FiscalInvoice" && x.ReferenceId == invoice.Id.ToString()).ToListAsync());
        }

        var receiptRequest = new PostGoodsReceiptRequest(order.Id, inbound.Id, warehouseId, [new ReceiptLineRequest(order.Lines[0].Id, 100, 97, 94, 2, 1, "LOT-SYNTH-1", DateTimeOffset.UtcNow.AddYears(1))]);
        using var receiptMessage = new HttpRequestMessage(HttpMethod.Post, "/api/admin/supply-chain/receipts") { Content = JsonContent.Create(receiptRequest) };
        receiptMessage.Headers.Add("Idempotency-Key", "synthetic-receipt-100-97");
        var receiptResponse = await receiver.SendAsync(receiptMessage);
        receiptResponse.EnsureSuccessStatusCode();
        var receipt = (await receiptResponse.Content.ReadFromJsonAsync<GoodsReceiptDto>())!;
        var receiptLine = Assert.Single(receipt.Lines);
        Assert.Equal(3, receiptLine.MissingQuantity);
        Assert.Equal(94, receiptLine.AcceptedQuantity);
        Assert.Equal(2, receiptLine.DamagedQuantity);
        Assert.Equal(1, receiptLine.QuarantinedQuantity);

        using var replayMessage = new HttpRequestMessage(HttpMethod.Post, "/api/admin/supply-chain/receipts") { Content = JsonContent.Create(receiptRequest) };
        replayMessage.Headers.Add("Idempotency-Key", "synthetic-receipt-100-97");
        var replayResponse = await receiver.SendAsync(replayMessage);
        replayResponse.EnsureSuccessStatusCode();
        Assert.True((await replayResponse.Content.ReadFromJsonAsync<GoodsReceiptDto>())!.IdempotencyReplayed);
        var changedReceipt = receiptRequest with { Lines = [receiptRequest.Lines[0] with { AcceptedQuantity = 93, QuarantinedQuantity = 2 }] };
        using var changedReplayMessage = new HttpRequestMessage(HttpMethod.Post, "/api/admin/supply-chain/receipts") { Content = JsonContent.Create(changedReceipt) };
        changedReplayMessage.Headers.Add("Idempotency-Key", "synthetic-receipt-100-97");
        Assert.Equal(HttpStatusCode.Conflict, (await receiver.SendAsync(changedReplayMessage)).StatusCode);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Commerce.Infrastructure.CommerceDbContext>();
            Assert.Equal(initialInventory + 94, await db.Inventory.Where(x => x.VariantId == variantId).Select(x => x.QuantityOnHand).SingleAsync());
            Assert.Equal(initialWarehouseStock + 94, await db.WarehouseStocks.Where(x => x.WarehouseId == warehouseId && x.VariantId == variantId).Select(x => x.OnHand).SingleAsync());
            var ledger = await db.InventoryLedgerEntries.Where(x => x.ReferenceType == "GoodsReceipt" && x.ReferenceId == receipt.Id.ToString()).OrderBy(x => x.State).ToListAsync();
            Assert.Equal(3, ledger.Count);
            Assert.Contains(ledger, x => x.State == Commerce.Domain.InventoryState.Available && x.QuantityDelta == 94);
            Assert.Contains(ledger, x => x.State == Commerce.Domain.InventoryState.Damaged && x.QuantityDelta == 2);
            Assert.Contains(ledger, x => x.State == Commerce.Domain.InventoryState.Quarantined && x.QuantityDelta == 1);
            Assert.DoesNotContain(ledger, x => x.QuantityDelta == 100);
            var balances = await db.InventoryBalances.Where(x => x.WarehouseId == warehouseId && x.VariantId == variantId).ToListAsync();
            Assert.Contains(balances, x => x.State == Commerce.Domain.InventoryState.Available && x.Quantity == 94);
            Assert.Contains(balances, x => x.State == Commerce.Domain.InventoryState.Damaged && x.Quantity == 2);
            Assert.Contains(balances, x => x.State == Commerce.Domain.InventoryState.Quarantined && x.Quantity == 1);
        }

        var matchResponse = await receiver.PostAsync($"/api/admin/supply-chain/invoices/{invoice.Id}/match", null);
        matchResponse.EnsureSuccessStatusCode();
        var match = (await matchResponse.Content.ReadFromJsonAsync<InvoiceMatchDto>())!;
        Assert.Equal("Exception", match.Status);
        Assert.Contains(match.Exceptions, x => x.Contains("exceeds physically received", StringComparison.Ordinal));

        var acceptedInvoiceRequest = invoiceRequest with { ExternalNumber = "INV-SYNTH-97", TotalAmount = 970m, RawPayload = "invoice|INV-SYNTH-97|97", Lines = [new InvoiceLineRequest(order.Lines[0].Id, "SUP-100", 97, 10m)] };
        var acceptedInvoiceResponse = await buyer.PostAsJsonAsync("/api/admin/supply-chain/invoices", acceptedInvoiceRequest);
        acceptedInvoiceResponse.EnsureSuccessStatusCode();
        var acceptedInvoice = (await acceptedInvoiceResponse.Content.ReadFromJsonAsync<FiscalInvoiceDto>())!;
        var acceptedMatchResponse = await receiver.PostAsync($"/api/admin/supply-chain/invoices/{acceptedInvoice.Id}/match", null);
        acceptedMatchResponse.EnsureSuccessStatusCode();
        Assert.Equal("Matched", (await acceptedMatchResponse.Content.ReadFromJsonAsync<InvoiceMatchDto>())!.Status);

        var duplicateClaimRequest = invoiceRequest with { ExternalNumber = "INV-SYNTH-EXTRA", TotalAmount = 10m, RawPayload = "invoice|INV-SYNTH-EXTRA|1", Lines = [new InvoiceLineRequest(order.Lines[0].Id, "SUP-100", 1, 10m)] };
        var duplicateClaimResponse = await buyer.PostAsJsonAsync("/api/admin/supply-chain/invoices", duplicateClaimRequest);
        duplicateClaimResponse.EnsureSuccessStatusCode();
        var duplicateClaim = (await duplicateClaimResponse.Content.ReadFromJsonAsync<FiscalInvoiceDto>())!;
        var duplicateMatchResponse = await receiver.PostAsync($"/api/admin/supply-chain/invoices/{duplicateClaim.Id}/match", null);
        duplicateMatchResponse.EnsureSuccessStatusCode();
        var duplicateMatch = (await duplicateMatchResponse.Content.ReadFromJsonAsync<InvoiceMatchDto>())!;
        Assert.Equal("Exception", duplicateMatch.Status);
        Assert.Contains(duplicateMatch.Exceptions, x => x.Contains("cumulative invoiced quantity", StringComparison.Ordinal));

        var cycleCountResponse = await counter.PostAsJsonAsync("/api/admin/supply-chain/cycle-counts", new CreateCycleCountRequest(warehouseId, [new CycleCountScopeRequest(variantId, null, receiptLine.LotId, "Available")]));
        cycleCountResponse.EnsureSuccessStatusCode();
        var cycleCount = (await cycleCountResponse.Content.ReadFromJsonAsync<CycleCountDto>())!;
        var cycleLine = Assert.Single(cycleCount.Lines);
        Assert.Null(cycleLine.ExpectedQuantity);
        var countTasks = (await counter.GetFromJsonAsync<IReadOnlyList<WarehouseTaskDto>>($"/api/admin/supply-chain/warehouse-tasks?warehouseId={warehouseId}"))!;
        var countTask = Assert.Single(countTasks, x => x.ReferenceId == cycleLine.Id.ToString());
        Assert.Equal(1, countTask.Quantity);
        var startCountResponse = await counter.PostAsync($"/api/admin/supply-chain/cycle-counts/{cycleCount.Id}/start", null);
        startCountResponse.EnsureSuccessStatusCode();
        var submitCountResponse = await counter.PostAsJsonAsync($"/api/admin/supply-chain/cycle-counts/{cycleCount.Id}/submit", new SubmitCycleCountRequest([new SubmitCycleCountLineRequest(cycleLine.Id, 93)]));
        submitCountResponse.EnsureSuccessStatusCode();
        var submittedCount = (await submitCountResponse.Content.ReadFromJsonAsync<CycleCountDto>())!;
        Assert.Equal(94, submittedCount.Lines[0].ExpectedQuantity);
        Assert.Equal(-1, submittedCount.Lines[0].Difference);
        Assert.Equal(HttpStatusCode.Forbidden, (await counter.PostAsync($"/api/admin/supply-chain/cycle-counts/{cycleCount.Id}/reconcile", null)).StatusCode);
        var reconcileResponse = await reconciler.PostAsync($"/api/admin/supply-chain/cycle-counts/{cycleCount.Id}/reconcile", null);
        reconcileResponse.EnsureSuccessStatusCode();
        Assert.Equal("Reconciled", (await reconcileResponse.Content.ReadFromJsonAsync<CycleCountDto>())!.Status);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Commerce.Infrastructure.CommerceDbContext>();
            Assert.Equal(initialInventory + 93, await db.Inventory.Where(x => x.VariantId == variantId).Select(x => x.QuantityOnHand).SingleAsync());
            Assert.Contains(await db.InventoryLedgerEntries.Where(x => x.ReferenceType == "CycleCount" && x.ReferenceId == cycleCount.Id.ToString()).ToListAsync(), x => x.State == Commerce.Domain.InventoryState.Available && x.QuantityDelta == -1);
            Assert.All(await db.WarehouseTasks.Where(x => x.ReferenceType == "CycleCountLine" && x.ReferenceId == cycleLine.Id.ToString()).ToListAsync(), x => Assert.Equal(Commerce.Domain.WarehouseTaskStatus.Completed, x.Status));
        }

        var staleCountResponse = await counter.PostAsJsonAsync("/api/admin/supply-chain/cycle-counts", new CreateCycleCountRequest(warehouseId, [new CycleCountScopeRequest(variantId, null, receiptLine.LotId, "Available")]));
        staleCountResponse.EnsureSuccessStatusCode();
        var staleCount = (await staleCountResponse.Content.ReadFromJsonAsync<CycleCountDto>())!;
        var staleLine = Assert.Single(staleCount.Lines);
        (await counter.PostAsync($"/api/admin/supply-chain/cycle-counts/{staleCount.Id}/start", null)).EnsureSuccessStatusCode();
        (await counter.PostAsJsonAsync($"/api/admin/supply-chain/cycle-counts/{staleCount.Id}/submit", new SubmitCycleCountRequest([new SubmitCycleCountLineRequest(staleLine.Id, 92)]))).EnsureSuccessStatusCode();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Commerce.Infrastructure.CommerceDbContext>();
            var balance = await db.InventoryBalances.SingleAsync(x => x.WarehouseId == warehouseId && x.VariantId == variantId && x.LotId == receiptLine.LotId && x.State == Commerce.Domain.InventoryState.Available);
            var aggregate = await db.Inventory.SingleAsync(x => x.VariantId == variantId);
            var stock = await db.WarehouseStocks.SingleAsync(x => x.WarehouseId == warehouseId && x.VariantId == variantId);
            balance.Quantity += 1; aggregate.QuantityOnHand += 1; stock.OnHand += 1;
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Conflict, (await reconciler.PostAsync($"/api/admin/supply-chain/cycle-counts/{staleCount.Id}/reconcile", null)).StatusCode);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Commerce.Infrastructure.CommerceDbContext>();
            Assert.Equal(initialInventory + 94, await db.Inventory.Where(x => x.VariantId == variantId).Select(x => x.QuantityOnHand).SingleAsync());
            Assert.Empty(await db.InventoryLedgerEntries.Where(x => x.ReferenceType == "CycleCount" && x.ReferenceId == staleCount.Id.ToString()).ToListAsync());
        }

        var destinationWarehouseId = Guid.CreateVersion7();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Commerce.Infrastructure.CommerceDbContext>();
            db.Warehouses.Add(new Commerce.Domain.Warehouse { Id = destinationWarehouseId, Code = "SYNTH-DEST", Name = "Synthetic destination", IsActive = true });
            await db.SaveChangesAsync();
        }
        var transferRequest = new CreateStockTransferRequest(warehouseId, destinationWarehouseId, [new CreateStockTransferLineRequest(variantId, receiptLine.LotId, 10)]);
        using var createTransfer = new HttpRequestMessage(HttpMethod.Post, "/api/admin/supply-chain/stock-transfers") { Content = JsonContent.Create(transferRequest) };
        createTransfer.Headers.Add("Idempotency-Key", "synthetic-transfer-create");
        var transferResponse = await transferOperator.SendAsync(createTransfer);
        transferResponse.EnsureSuccessStatusCode();
        var stockTransfer = (await transferResponse.Content.ReadFromJsonAsync<StockTransferDto>())!;
        using (var createReplay = new HttpRequestMessage(HttpMethod.Post, "/api/admin/supply-chain/stock-transfers") { Content = JsonContent.Create(transferRequest) })
        {
            createReplay.Headers.Add("Idempotency-Key", "synthetic-transfer-create");
            var createReplayResponse = await transferOperator.SendAsync(createReplay);
            createReplayResponse.EnsureSuccessStatusCode();
            Assert.True((await createReplayResponse.Content.ReadFromJsonAsync<StockTransferDto>())!.IdempotencyReplayed);
        }
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Commerce.Infrastructure.CommerceDbContext>();
            Assert.Equal(1, await db.StockTransfers.CountAsync(x => x.CreateIdempotencyKey == "synthetic-transfer-create"));
            var task = await db.WarehouseTasks.SingleAsync(x => x.ReferenceType == "StockTransferDispatch" && x.ReferenceId == stockTransfer.Lines[0].Id.ToString());
            Assert.Equal(10, task.Quantity);
            Assert.Equal(receiptLine.LotId, task.LotId);
        }
        using (var dispatch = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/supply-chain/stock-transfers/{stockTransfer.Id}/dispatch"))
        {
            dispatch.Headers.Add("Idempotency-Key", "synthetic-transfer-dispatch");
            var dispatchResponse = await transferOperator.SendAsync(dispatch);
            dispatchResponse.EnsureSuccessStatusCode();
            Assert.Equal("InTransit", (await dispatchResponse.Content.ReadFromJsonAsync<StockTransferDto>())!.Status);
        }
        using (var dispatchReplay = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/supply-chain/stock-transfers/{stockTransfer.Id}/dispatch"))
        {
            dispatchReplay.Headers.Add("Idempotency-Key", "synthetic-transfer-dispatch");
            var dispatchReplayResponse = await transferOperator.SendAsync(dispatchReplay);
            dispatchReplayResponse.EnsureSuccessStatusCode();
            Assert.True((await dispatchReplayResponse.Content.ReadFromJsonAsync<StockTransferDto>())!.IdempotencyReplayed);
        }
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Commerce.Infrastructure.CommerceDbContext>();
            Assert.Equal(initialInventory + 84, await db.Inventory.Where(x => x.VariantId == variantId).Select(x => x.QuantityOnHand).SingleAsync());
            Assert.Equal(10, await db.InventoryBalances.Where(x => x.WarehouseId == destinationWarehouseId && x.VariantId == variantId && x.LotId == receiptLine.LotId && x.State == Commerce.Domain.InventoryState.InTransit).Select(x => x.Quantity).SingleAsync());
        }
        using (var receive = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/supply-chain/stock-transfers/{stockTransfer.Id}/receive"))
        {
            receive.Headers.Add("Idempotency-Key", "synthetic-transfer-receive");
            var receiveResponse = await transferOperator.SendAsync(receive);
            receiveResponse.EnsureSuccessStatusCode();
            Assert.Equal("Received", (await receiveResponse.Content.ReadFromJsonAsync<StockTransferDto>())!.Status);
        }
        using (var receiveReplay = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/supply-chain/stock-transfers/{stockTransfer.Id}/receive"))
        {
            receiveReplay.Headers.Add("Idempotency-Key", "synthetic-transfer-receive");
            var receiveReplayResponse = await transferOperator.SendAsync(receiveReplay);
            receiveReplayResponse.EnsureSuccessStatusCode();
            Assert.True((await receiveReplayResponse.Content.ReadFromJsonAsync<StockTransferDto>())!.IdempotencyReplayed);
        }
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Commerce.Infrastructure.CommerceDbContext>();
            Assert.Equal(initialInventory + 94, await db.Inventory.Where(x => x.VariantId == variantId).Select(x => x.QuantityOnHand).SingleAsync());
            Assert.Equal(10, await db.WarehouseStocks.Where(x => x.WarehouseId == destinationWarehouseId && x.VariantId == variantId).Select(x => x.OnHand).SingleAsync());
            Assert.Equal(0, await db.InventoryBalances.Where(x => x.WarehouseId == destinationWarehouseId && x.VariantId == variantId && x.LotId == receiptLine.LotId && x.State == Commerce.Domain.InventoryState.InTransit).Select(x => x.Quantity).SingleAsync());
            Assert.Equal(10, await db.InventoryBalances.Where(x => x.WarehouseId == destinationWarehouseId && x.VariantId == variantId && x.LotId == receiptLine.LotId && x.State == Commerce.Domain.InventoryState.Available).Select(x => x.Quantity).SingleAsync());
            var tasks = await db.WarehouseTasks.Where(x => x.ReferenceId == stockTransfer.Lines[0].Id.ToString()).ToListAsync();
            Assert.Equal(2, tasks.Count);
            Assert.All(tasks, x => { Assert.Equal(Commerce.Domain.WarehouseTaskStatus.Completed, x.Status); Assert.Equal(10, x.CompletedQuantity); });
            var transferLedger = await db.InventoryLedgerEntries.Where(x => x.ReferenceType == "StockTransfer" && x.ReferenceId == stockTransfer.Id.ToString()).ToListAsync();
            Assert.Equal(4, transferLedger.Count);
            Assert.Contains(transferLedger, x => x.WarehouseId == warehouseId && x.State == Commerce.Domain.InventoryState.Available && x.QuantityDelta == -10);
            Assert.Contains(transferLedger, x => x.WarehouseId == destinationWarehouseId && x.State == Commerce.Domain.InventoryState.InTransit && x.QuantityDelta == 10);
            Assert.Contains(transferLedger, x => x.WarehouseId == destinationWarehouseId && x.State == Commerce.Domain.InventoryState.InTransit && x.QuantityDelta == -10);
            Assert.Contains(transferLedger, x => x.WarehouseId == destinationWarehouseId && x.State == Commerce.Domain.InventoryState.Available && x.QuantityDelta == 10);
        }
    }

    [Fact]
    public async Task Supplier_portal_queries_and_commands_are_scoped_by_persisted_membership()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync();
        await using var factory = CreateFactory(postgres.GetConnectionString(), cacheEnabled: false);
        SupplierDto supplierA;
        SupplierDto supplierB;
        PurchaseOrderDto orderA;
        PurchaseOrderDto orderB;
        RfqDto rfq;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Commerce.Infrastructure.CommerceDbContext>();
            var service = scope.ServiceProvider.GetRequiredService<SupplyChainService>();
            var warehouseId = await db.Warehouses.Select(x => x.Id).FirstAsync();
            var variantId = await db.ProductVariants.Select(x => x.Id).FirstAsync();
            supplierA = await service.CreateSupplierAsync(new("TENANT-A", "Tenant A Supplier", "US", null), "setup", CancellationToken.None);
            supplierB = await service.CreateSupplierAsync(new("TENANT-B", "Tenant B Supplier", "US", null), "setup", CancellationToken.None);
            var sourceA = await service.CreateSourceAsync(new(supplierA.Id, variantId, "A-SKU", "USD", 5m, 3, 1, 1, 95m), "setup", CancellationToken.None);
            var sourceB = await service.CreateSourceAsync(new(supplierB.Id, variantId, "B-SKU", "USD", 5m, 3, 1, 1, 95m), "setup", CancellationToken.None);
            orderA = await service.CreatePurchaseOrderAsync(new(supplierA.Id, warehouseId, DateTimeOffset.UtcNow.AddDays(3), [new(sourceA.Id, 10)]), "buyer", CancellationToken.None);
            orderB = await service.CreatePurchaseOrderAsync(new(supplierB.Id, warehouseId, DateTimeOffset.UtcNow.AddDays(3), [new(sourceB.Id, 10)]), "buyer", CancellationToken.None);
            orderA = await service.ApprovePurchaseOrderAsync(orderA.Id, "approver", CancellationToken.None);
            orderB = await service.ApprovePurchaseOrderAsync(orderB.Id, "approver", CancellationToken.None);
            var userA = new Commerce.Domain.ApplicationUser { Id = Guid.CreateVersion7(), IdentityIssuer = TestIssuer, ExternalSubject = "supplier-tenant-a", CreatedAt = DateTimeOffset.UtcNow };
            var userB = new Commerce.Domain.ApplicationUser { Id = Guid.CreateVersion7(), IdentityIssuer = TestIssuer, ExternalSubject = "supplier-tenant-b", CreatedAt = DateTimeOffset.UtcNow };
            db.ApplicationUsers.AddRange(userA, userB);
            db.SupplierUsers.AddRange(
                new Commerce.Domain.SupplierUser { SupplierOrganizationId = supplierA.Id, ApplicationUserId = userA.Id, CreatedAt = DateTimeOffset.UtcNow },
                new Commerce.Domain.SupplierUser { SupplierOrganizationId = supplierB.Id, ApplicationUserId = userB.Id, CreatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
            rfq = await service.CreateRfqAsync(new CreateRfqRequest(warehouseId, "USD", DateTimeOffset.UtcNow.AddDays(2), [supplierA.Id, supplierB.Id], [new CreateRfqLineRequest(variantId, 10)]), "buyer", CancellationToken.None);
            rfq = await service.OpenRfqAsync(rfq.Id, "buyer", CancellationToken.None);
        }

        using var portalA = BearerClient(factory, Token("supplier-tenant-a", ["supplier-portal"]));
        using var portalB = BearerClient(factory, Token("supplier-tenant-b", ["supplier-portal"]));
        var visibleA = (await portalA.GetFromJsonAsync<IReadOnlyList<PurchaseOrderDto>>("/api/supplier/purchase-orders"))!;
        Assert.Single(visibleA);
        Assert.Equal(orderA.Id, visibleA[0].Id);
        var crossTenantAsn = new CreateInboundShipmentRequest(orderB.Id, null, null, null, [new CreateInboundShipmentLineRequest(orderB.Lines[0].Id, 10)]);
        using (var message = new HttpRequestMessage(HttpMethod.Post, "/api/supplier/shipments") { Content = JsonContent.Create(crossTenantAsn) })
        {
            message.Headers.Add("Idempotency-Key", "cross-tenant-asn");
            Assert.Equal(HttpStatusCode.NotFound, (await portalA.SendAsync(message)).StatusCode);
        }

        const string sharedPayload = "invoice|SHARED-100|10";
        var invoiceA = new InvoiceIngestionRequest(supplierA.Id, orderA.Id, "GENERIC", "SHARED-100", "USD", 50m, DateTimeOffset.UtcNow, null, sharedPayload, [new InvoiceLineRequest(orderA.Lines[0].Id, "A-SKU", 10, 5m)]);
        var invoiceB = new InvoiceIngestionRequest(supplierB.Id, orderB.Id, "GENERIC", "SHARED-100", "USD", 50m, DateTimeOffset.UtcNow, null, sharedPayload, [new InvoiceLineRequest(orderB.Lines[0].Id, "B-SKU", 10, 5m)]);
        Assert.Equal(HttpStatusCode.BadRequest, (await portalA.PostAsJsonAsync("/api/supplier/invoices", invoiceB)).StatusCode);
        var responseA = await portalA.PostAsJsonAsync("/api/supplier/invoices", invoiceA);
        var responseB = await portalB.PostAsJsonAsync("/api/supplier/invoices", invoiceB);
        responseA.EnsureSuccessStatusCode();
        responseB.EnsureSuccessStatusCode();
        var createdA = (await responseA.Content.ReadFromJsonAsync<FiscalInvoiceDto>())!;
        Assert.NotEqual(createdA.Id, (await responseB.Content.ReadFromJsonAsync<FiscalInvoiceDto>())!.Id);
        var replayA = await portalA.PostAsJsonAsync("/api/supplier/invoices", invoiceA);
        replayA.EnsureSuccessStatusCode();
        Assert.Equal(createdA.Id, (await replayA.Content.ReadFromJsonAsync<FiscalInvoiceDto>())!.Id);
        Assert.Equal(HttpStatusCode.Conflict, (await portalA.PostAsJsonAsync("/api/supplier/invoices", invoiceA with { TotalAmount = 55m })).StatusCode);

        var visibleRfqsA = (await portalA.GetFromJsonAsync<IReadOnlyList<RfqDto>>("/api/supplier/rfqs"))!;
        var supplierView = Assert.Single(visibleRfqsA, x => x.Id == rfq.Id);
        Assert.Equal([supplierA.Id], supplierView.SupplierOrganizationIds);
        Assert.Empty(supplierView.CreatedBy);
        var quotationAResponse = await portalA.PostAsJsonAsync($"/api/supplier/rfqs/{rfq.Id}/quotations", new SubmitQuotationRequest("USD", [new SubmitQuotationLineRequest(rfq.Lines[0].Id, "A-SKU", 4m, 10, 1, 1, 92m)]));
        var quotationBResponse = await portalB.PostAsJsonAsync($"/api/supplier/rfqs/{rfq.Id}/quotations", new SubmitQuotationRequest("USD", [new SubmitQuotationLineRequest(rfq.Lines[0].Id, "B-SKU", 5m, 2, 1, 1, 99m)]));
        quotationAResponse.EnsureSuccessStatusCode();
        quotationBResponse.EnsureSuccessStatusCode();
        var quotationA = (await quotationAResponse.Content.ReadFromJsonAsync<SupplierQuotationDto>())!;
        var quotationB = (await quotationBResponse.Content.ReadFromJsonAsync<SupplierQuotationDto>())!;
        Assert.True(quotationA.Total.Amount < quotationB.Total.Amount);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<SupplyChainService>();
            var selfAward = await Assert.ThrowsAsync<CommerceException>(() => service.AwardQuotationAsync(rfq.Id, new(quotationB.Id, "Faster delivery is required."), "buyer", CancellationToken.None));
            Assert.Equal(403, selfAward.StatusCode);
            var award = await service.AwardQuotationAsync(rfq.Id, new(quotationB.Id, "Faster delivery is required."), "independent-approver", CancellationToken.None);
            Assert.Equal(supplierB.Id, award.DraftPurchaseOrder.SupplierOrganizationId);
            Assert.Equal(quotationB.Id, award.DraftPurchaseOrder.SourceQuotationId);
            Assert.Equal("Draft", award.DraftPurchaseOrder.Status);
            Assert.Equal(5m, award.DraftPurchaseOrder.Lines[0].UnitCost.Amount);
            Assert.NotNull(award.DraftPurchaseOrder.Lines[0].SourceQuotationLineId);
            Assert.Equal(2, award.DraftPurchaseOrder.Lines[0].LeadTimeDays);
            Assert.Equal(1, award.DraftPurchaseOrder.Lines[0].MinimumOrderQuantity);
            Assert.Equal(1, award.DraftPurchaseOrder.Lines[0].OrderMultiple);
            Assert.Equal(99m, award.DraftPurchaseOrder.Lines[0].ReliabilityPercent);
            Assert.Equal("Awarded", award.Quotation.Status);
            Assert.Equal("Rejected", (await service.GetQuotationsAsync(rfq.Id, CancellationToken.None)).Single(x => x.Id == quotationA.Id).Status);
        }
    }

    [Fact]
    public async Task OpenApi_marks_protected_operations_with_bearer_security()
    {
        await using var factory = CreateFactory("Host=127.0.0.1;Port=1;Database=unused;Username=unused;Password=unused", cacheEnabled: false, applyMigrations: false);
        using var client = factory.CreateClient();
        using var document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
        var root = document.RootElement;

        Assert.True(root.GetProperty("components").GetProperty("securitySchemes").TryGetProperty("Bearer", out _));
        Assert.True(root.GetProperty("paths").GetProperty("/api/cart").GetProperty("get").TryGetProperty("security", out _));
        Assert.False(root.GetProperty("paths").GetProperty("/api/catalog/categories").GetProperty("get").TryGetProperty("security", out _));
    }

    [Fact]
    public async Task Concurrent_checkout_of_last_unit_creates_only_one_order()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync();
        await using var factory = CreateFactory(postgres.GetConnectionString(), cacheEnabled: false);
        using var shopperA = factory.CreateClient();
        using var shopperB = factory.CreateClient();
        var product = await shopperA.GetFromJsonAsync<StorefrontProductDto>("/api/storefront/products/noise-cancelling-headphones");
        var variantId = product!.Product.Variants[0].Id;

        shopperA.DefaultRequestHeaders.Add("X-Customer-Id", "concurrent-a");
        shopperB.DefaultRequestHeaders.Add("X-Customer-Id", "concurrent-b");
        (await shopperA.PutAsJsonAsync("/api/cart/items", new SetCartItemRequest(variantId, 1))).EnsureSuccessStatusCode();
        (await shopperB.PutAsJsonAsync("/api/cart/items", new SetCartItemRequest(variantId, 1))).EnsureSuccessStatusCode();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Commerce.Infrastructure.CommerceDbContext>();
            var inventory = await db.Inventory.FindAsync([variantId]);
            inventory!.QuantityOnHand = 1;
            await db.SaveChangesAsync();
        }

        var checkout = new CheckoutRequest(new AddressRequest("Concurrent Shopper", "10 Main Street", null, "Austin", "TX", "78701", "US"));
        static HttpRequestMessage Request(CheckoutRequest body, string key)
        {
            var message = new HttpRequestMessage(HttpMethod.Post, "/api/checkout/confirm") { Content = JsonContent.Create(body) };
            message.Headers.Add("Idempotency-Key", key);
            return message;
        }
        using var requestA = Request(checkout, "last-unit-a");
        using var requestB = Request(checkout, "last-unit-b");
        var responses = await Task.WhenAll(shopperA.SendAsync(requestA), shopperB.SendAsync(requestB));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Created);
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Concurrent_checkout_of_the_same_cart_with_different_keys_creates_one_order()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync();
        await using var factory = CreateFactory(postgres.GetConnectionString(), cacheEnabled: false);
        using var shopper = factory.CreateClient();
        shopper.DefaultRequestHeaders.Add("X-Customer-Id", "same-cart-concurrent");
        var product = await shopper.GetFromJsonAsync<StorefrontProductDto>("/api/storefront/products/noise-cancelling-headphones");
        var variantId = product!.Product.Variants[0].Id;
        (await shopper.PutAsJsonAsync("/api/cart/items", new SetCartItemRequest(variantId, 1))).EnsureSuccessStatusCode();

        var checkout = new CheckoutRequest(new AddressRequest("Concurrent Shopper", "10 Main Street", null, "Austin", "TX", "78701", "US"));
        static HttpRequestMessage Request(CheckoutRequest body, string key)
        {
            var message = new HttpRequestMessage(HttpMethod.Post, "/api/checkout/confirm") { Content = JsonContent.Create(body) };
            message.Headers.Add("Idempotency-Key", key);
            return message;
        }

        using var requestA = Request(checkout, "same-cart-a");
        using var requestB = Request(checkout, "same-cart-b");
        var responses = await Task.WhenAll(shopper.SendAsync(requestA), shopper.SendAsync(requestB));
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.BadRequest);
        var orders = await shopper.GetFromJsonAsync<IReadOnlyList<OrderDto>>("/api/orders");
        Assert.Single(orders!);
        foreach (var response in responses) response.Dispose();
    }

    [Fact]
    public async Task Storefront_falls_back_to_postgres_when_distributed_cache_is_unavailable()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync();
        await using var factory = CreateFactory(postgres.GetConnectionString(), cacheEnabled: true);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/storefront/home");
        response.EnsureSuccessStatusCode();
        var home = await response.Content.ReadFromJsonAsync<HomeDto>();
        Assert.NotEmpty(home!.Rails.SelectMany(x => x.Products));
    }

    [Fact]
    public async Task PostgreSql_outage_fails_readiness_and_returns_a_safe_service_unavailable_problem()
    {
        await using var factory = CreateFactory("Host=127.0.0.1;Port=1;Database=missing;Username=missing;Password=missing;Timeout=1;Command Timeout=1", cacheEnabled: false, applyMigrations: false);
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/health/ready")).StatusCode);
        var response = await client.GetAsync("/api/catalog/products?pageSize=1");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("5", response.Headers.RetryAfter!.Delta?.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? response.Headers.GetValues("Retry-After").Single());
        var problem = await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>();
        Assert.True(problem!.Extensions.TryGetValue("code", out var code));
        Assert.Equal("database_unavailable", code!.ToString());
        Assert.DoesNotContain("Npgsql", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Checkout_rate_limit_rejects_bursts_with_retry_after()
    {
        await using var factory = CreateFactory("Host=127.0.0.1;Port=1;Database=unused;Username=unused;Password=unused", cacheEnabled: false, applyMigrations: false);
        using var client = factory.CreateClient();
        var checkout = new CheckoutRequest(new AddressRequest("Rate Limited", "1 Main Street", null, "Austin", "TX", "78701", "US"));
        var responses = new List<HttpResponseMessage>();
        for (var i = 0; i < 6; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/checkout/confirm") { Content = JsonContent.Create(checkout) };
            request.Headers.Add("Idempotency-Key", $"rate-limit-{i}");
            responses.Add(await client.SendAsync(request));
        }

        Assert.All(responses.Take(5), response => Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode));
        Assert.Equal(HttpStatusCode.TooManyRequests, responses[5].StatusCode);
        Assert.True(responses[5].Headers.Contains("Retry-After"));
        foreach (var response in responses) response.Dispose();
    }

    [Fact]
    public async Task Category_queries_return_only_their_subtree_and_recommendations_stay_separate()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync();
        await using var factory = CreateFactory(postgres.GetConnectionString(), cacheEnabled: false);
        using var client = factory.CreateClient();

        async Task<string[]> Titles(string category, string? q = null) =>
            (await client.GetFromJsonAsync<ProductPageDto>($"/api/catalog/products?category={category}&pageSize=100{(q is null ? "" : $"&q={q}")}"))!
                .Items.Select(x => x.Title).Order().ToArray();

        string[] electronics = ["Cable Management Kit", "E-Reader Cover", "Ergonomic Mouse", "Mechanical Keyboard", "Noise-Cancelling Headphones", "Portable Speaker", "Studio Microphone", "USB-C Travel Hub", "Webcam Light", "Wireless Charging Stand", "Adjustable Laptop Stand"];
        string[] books = ["Cookbook for Weeknights", "Distributed Systems Field Guide", "Platform Engineering Handbook"];
        Assert.Equal(electronics.Order(), await Titles("electronics"));
        Assert.Equal(books.Order(), await Titles("books"));
        Assert.DoesNotContain("Cast Iron Skillet", await Titles("books"));
        Assert.Empty(await Titles("electronics", "handbook"));
        Assert.Empty(await Titles("no-such-category"));

        // A hierarchy the seed does not have: Electronics > Audio, Appliances > Refrigerators.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Commerce.Infrastructure.CommerceDbContext>();
            var electronicsId = (await db.Categories.SingleAsync(x => x.Slug == "electronics")).Id;
            var audio = new Commerce.Domain.Category { Id = Guid.NewGuid(), ParentId = electronicsId, Slug = "audio", Name = "Audio" };
            var appliances = new Commerce.Domain.Category { Id = Guid.NewGuid(), Slug = "appliances", Name = "Appliances" };
            var refrigerators = new Commerce.Domain.Category { Id = Guid.NewGuid(), ParentId = appliances.Id, Slug = "refrigerators", Name = "Refrigerators" };
            db.Categories.AddRange(audio, appliances, refrigerators);
            AddProduct(db, audio, "Reference Studio Headphones");
            AddProduct(db, refrigerators, "Frost 500L French-Door Refrigerator");
            AddProduct(db, refrigerators, "Compact 90L Bar Refrigerator");
            AddProduct(db, appliances, "Chest Deep Freezer");
            await db.SaveChangesAsync();
            await scope.ServiceProvider.GetRequiredService<IReadModelCache>().RemoveByTagAsync("categories", CancellationToken.None);
        }

        Assert.Equal(electronics.Append("Reference Studio Headphones").Order(), await Titles("electronics"));
        Assert.Equal(["Reference Studio Headphones"], await Titles("audio"));
        Assert.Equal(["Chest Deep Freezer", "Compact 90L Bar Refrigerator", "Frost 500L French-Door Refrigerator"], await Titles("appliances"));
        Assert.Equal(["Compact 90L Bar Refrigerator", "Frost 500L French-Door Refrigerator"], await Titles("refrigerators"));
        Assert.Equal(books.Order(), await Titles("books"));

        // Recommendations come from the product's own category and never alter category results.
        var fridge = await client.GetFromJsonAsync<StorefrontProductDto>("/api/storefront/products/frost-500l-french-door-refrigerator");
        Assert.Equal(["Compact 90L Bar Refrigerator"], fridge!.Recommendations.Select(x => x.Title));
        var headphones = await client.GetFromJsonAsync<StorefrontProductDto>("/api/storefront/products/noise-cancelling-headphones");
        Assert.NotEmpty(headphones!.Recommendations);
        Assert.All(headphones.Recommendations, x => Assert.Contains(x.Title, electronics));
        Assert.DoesNotContain(headphones.Recommendations, x => x.Title == "Noise-Cancelling Headphones");
        Assert.Equal(["Compact 90L Bar Refrigerator", "Frost 500L French-Door Refrigerator"], await Titles("refrigerators"));

        // Databases seeded by the old round-robin seeder are repaired by FixSeedProductCategories.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Commerce.Infrastructure.CommerceDbContext>();
            var migrator = db.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>();
            // Rolling back also removes later schema, so read the category with SQL rather than through the API.
            async Task<string> HandbookCategory() => await db.Database.SqlQueryRaw<string>(
                "SELECT c.\"Slug\" AS \"Value\" FROM products p JOIN categories c ON c.\"Id\" = p.\"CategoryId\" WHERE p.\"Title\" = 'Platform Engineering Handbook'").SingleAsync();
            await migrator.MigrateAsync("20260921140928_AddPaymentOrchestration");
            Assert.Equal("electronics", await HandbookCategory());
            await migrator.MigrateAsync();
            Assert.Equal("books", await HandbookCategory());
        }
        Assert.Equal(books.Order(), await Titles("books"));
        Assert.Equal(electronics.Append("Reference Studio Headphones").Order(), await Titles("electronics"));
    }

    [Fact]
    public async Task Suggestions_match_mid_title_stay_in_scope_and_cards_carry_product_details()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync();
        await using var factory = CreateFactory(postgres.GetConnectionString(), cacheEnabled: false);
        using var client = factory.CreateClient();

        async Task<string[]> Suggest(string query) => (await client.GetFromJsonAsync<SuggestionDto[]>($"/api/search/suggestions?q={query}"))!.Select(x => x.Value).ToArray();

        Assert.Contains("Noise-Cancelling Headphones", await Suggest("head"));
        Assert.Contains("Platform Engineering Handbook", await Suggest("hand"));
        Assert.DoesNotContain("Platform Engineering Handbook", await Suggest("hand&category=electronics"));
        Assert.Contains("Platform Engineering Handbook", await Suggest("hand&category=books"));
        Assert.Empty(await Suggest("%25%25")); // "%%" is text to match, not a wildcard for everything
        Assert.Empty(await Suggest("__"));

        var electronics = await client.GetFromJsonAsync<ProductPageDto>("/api/catalog/products?category=electronics&pageSize=100");
        Assert.Equal(electronics!.TotalCount, electronics.Brands.Sum(x => x.Count));
        var brand = electronics.Brands[0];
        var filtered = await client.GetFromJsonAsync<ProductPageDto>($"/api/catalog/products?category=electronics&brand={brand.Value}&pageSize=100");
        Assert.Equal(brand.Count, filtered!.TotalCount);
        Assert.Equal(electronics.Brands.Select(x => x.Value), filtered.Brands.Select(x => x.Value)); // the chosen brand keeps its alternatives visible

        var headphones = Assert.Single(electronics.Items, x => x.Title == "Noise-Cancelling Headphones");
        Assert.Equal("headphones", headphones.Kind);
        Assert.Equal([new SpecDto("Type", "Over-ear"), new SpecDto("Noise cancelling", "Adaptive ANC"), new SpecDto("Battery", "30 hours")], headphones.Highlights);
        var book = (await client.GetFromJsonAsync<StorefrontProductDto>("/api/storefront/products/platform-engineering-handbook"))!.Product;
        Assert.Equal(("book", "books"), (book.Kind, book.CategorySlug));
        Assert.Contains(new SpecDto("ISBN", "979-8-88888-001-4"), book.Specifications);
    }

    private static void AddProduct(Commerce.Infrastructure.CommerceDbContext db, Commerce.Domain.Category category, string title)
    {
        var now = DateTimeOffset.UtcNow;
        var product = new Commerce.Domain.Product { Id = Guid.NewGuid(), CategoryId = category.Id, Slug = title.ToLowerInvariant().Replace(' ', '-'), Title = title, Brand = "Test", Description = title, CreatedAt = now, UpdatedAt = now };
        var variantId = Guid.NewGuid();
        product.Variants.Add(new Commerce.Domain.ProductVariant { Id = variantId, ProductId = product.Id, Sku = $"T-{variantId:N}"[..20], Name = "Standard", Price = 100m, Inventory = new Commerce.Domain.InventoryItem { VariantId = variantId, QuantityOnHand = 5, UpdatedAt = now } });
        db.Products.Add(product);
    }

    private static WebApplicationFactory<Program> CreateFactory(string connectionString, bool cacheEnabled, bool applyMigrations = true) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("DATABASE_URL", connectionString);
            builder.UseSetting("APPLY_MIGRATIONS", applyMigrations.ToString());
            builder.UseSetting("ALLOW_DEVELOPMENT_IDENTITY", "true");
            builder.UseSetting("AUTH_AUTHORITY", TestIssuer);
            builder.UseSetting("AUTH_AUDIENCE", TestAudience);
            builder.UseSetting("CACHE_ENABLED", cacheEnabled.ToString());
            builder.UseSetting("CACHE_PROVIDER", "valkey");
            builder.UseSetting("CACHE_CONNECTION", "127.0.0.1:6399,connectTimeout=100,connectRetry=0,abortConnect=false");
            builder.ConfigureServices(services => services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                var configuration = new OpenIdConnectConfiguration { Issuer = TestIssuer };
                configuration.SigningKeys.Add(SigningKey);
                options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
            }));
        });

    private static HttpClient BearerClient(WebApplicationFactory<Program> factory, string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return client;
    }

    private static string Token(string subject, string[]? roles = null, DateTime? expires = null, string? issuer = null, string? audience = null, SecurityKey? signingKey = null, bool malformedRoles = false)
    {
        var expiresAt = expires ?? DateTime.UtcNow.AddMinutes(5);
        var claims = new List<Claim>
        {
            new("sub", subject),
            new("typ", "Bearer"),
            new("name", subject)
        };
        if (malformedRoles)
            claims.Add(new Claim("resource_access", "not-json"));
        else if (roles is { Length: > 0 })
            claims.Add(new Claim("resource_access", JsonSerializer.Serialize(new Dictionary<string, object> { [TestAudience] = new { roles } }), JsonClaimValueTypes.Json));

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer ?? TestIssuer,
            Audience = audience ?? TestAudience,
            Subject = new ClaimsIdentity(claims),
            NotBefore = expiresAt < DateTime.UtcNow ? expiresAt.AddMinutes(-5) : DateTime.UtcNow.AddMinutes(-1),
            Expires = expiresAt,
            SigningCredentials = new SigningCredentials(signingKey ?? SigningKey, SecurityAlgorithms.RsaSha256),
            TokenType = "JWT"
        };
        return new JwtSecurityTokenHandler().CreateEncodedJwt(descriptor);
    }
}
