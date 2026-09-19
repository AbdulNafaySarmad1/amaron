using System.Net;
using System.Net.Http.Json;
using Commerce.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;

namespace Commerce.IntegrationTests;

public sealed class ShopperJourneyTests
{
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

        var home = await client.GetFromJsonAsync<HomeDto>("/api/storefront/home");
        Assert.NotNull(home);
        var card = Assert.Single(home.Rails, rail => rail.Id == "featured").Products[0];
        var product = await client.GetFromJsonAsync<StorefrontProductDto>($"/api/storefront/products/{card.Slug}");
        Assert.NotNull(product);
        var variant = product.Product.Variants[0];

        client.DefaultRequestHeaders.Add("X-Customer-Id", "shopper-1");
        var cartResponse = await client.PutAsJsonAsync("/api/cart/items", new SetCartItemRequest(variant.Id, 2));
        cartResponse.EnsureSuccessStatusCode();
        var cart = await cartResponse.Content.ReadFromJsonAsync<CartMutationDto>();
        Assert.Equal(2, cart!.TotalQuantity);
        Assert.Equal(variant.Price.Amount * 2, cart.Subtotal.Amount);

        const string idempotencyKey = "integration-checkout-1";
        var checkoutRequest = new CheckoutRequest(new AddressRequest("Ada Shopper", "1 Market Street", null, "Seattle", "WA", "98101", "US"));
        using var firstRequest = new HttpRequestMessage(HttpMethod.Post, "/api/checkout/confirm") { Content = JsonContent.Create(checkoutRequest) };
        firstRequest.Headers.Add("Idempotency-Key", idempotencyKey);
        var firstResponse = await client.SendAsync(firstRequest);
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        var firstCheckout = await firstResponse.Content.ReadFromJsonAsync<CheckoutResultDto>();
        Assert.False(firstCheckout!.IdempotencyReplayed);
        Assert.Equal(variant.Price.Amount * 2, firstCheckout.Order.Subtotal.Amount);

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

    private static WebApplicationFactory<Program> CreateFactory(string connectionString, bool cacheEnabled) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("DATABASE_URL", connectionString);
            builder.UseSetting("APPLY_MIGRATIONS", "true");
            builder.UseSetting("CACHE_ENABLED", cacheEnabled.ToString());
            builder.UseSetting("CACHE_PROVIDER", "valkey");
            builder.UseSetting("CACHE_CONNECTION", "127.0.0.1:6399,connectTimeout=100,connectRetry=0,abortConnect=false");
        });
}
