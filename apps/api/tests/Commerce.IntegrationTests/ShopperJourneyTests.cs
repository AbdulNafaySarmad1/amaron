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

        var search = await client.GetFromJsonAsync<ProductPageDto>("/api/catalog/products?q=keyboard&category=home-kitchen&sort=price-asc&pageSize=5");
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
