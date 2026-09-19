using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.IO.Compression;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Commerce.Application;
using Commerce.Contracts;
using Commerce.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddEnvironmentVariables();
builder.Logging.AddJsonConsole(options => options.IncludeScopes = true);
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
{
    context.ProblemDetails.Extensions["traceId"] = Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
    context.ProblemDetails.Extensions.TryAdd("code", "request_failed");
});
builder.Services.AddOpenApi();
builder.Services.AddCommerceInfrastructure(builder.Configuration);
builder.Services.AddScoped<CatalogService>();
builder.Services.AddScoped<CartService>();
builder.Services.AddScoped<CheckoutService>();
builder.Services.AddScoped<StorefrontService>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(new ActivitySource("Commerce.Api"));
builder.Services.AddSingleton(new Meter("Commerce.Api"));
builder.Services.AddHealthChecks().AddDbContextCheck<CommerceDbContext>("postgresql", tags: ["ready"]);
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(["application/problem+json"]);
});
builder.Services.Configure<BrotliCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);
builder.Services.AddOutputCache(options =>
{
    options.AddPolicy("public-short", policy => policy.Expire(TimeSpan.FromSeconds(30)).Tag("public-storefront"));
    options.AddPolicy("public-product", policy => policy.Expire(TimeSpan.FromMinutes(1)).SetVaryByRouteValue("slug").Tag("public-products"));
});
builder.Services.AddRequestTimeouts(options =>
{
    options.AddPolicy("autocomplete", TimeSpan.FromSeconds(2));
    options.AddPolicy("catalog", TimeSpan.FromSeconds(5));
    options.AddPolicy("search", TimeSpan.FromSeconds(8));
    options.AddPolicy("checkout", TimeSpan.FromSeconds(20));
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.ContentType = "application/problem+json";
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry)) context.HttpContext.Response.Headers.RetryAfter = Math.Ceiling(retry.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
        await context.HttpContext.Response.WriteAsJsonAsync(new ProblemDetails { Type = "https://commerce.example/problems/rate-limit", Title = "Rate limit exceeded", Status = 429, Extensions = { ["code"] = "rate_limit_exceeded", ["traceId"] = Activity.Current?.Id ?? context.HttpContext.TraceIdentifier } }, token);
    };
    options.AddPolicy("catalog", context => RateLimitPartition.GetTokenBucketLimiter(ClientKey(context), _ => new TokenBucketRateLimiterOptions { TokenLimit = 120, TokensPerPeriod = 120, ReplenishmentPeriod = TimeSpan.FromMinutes(1), AutoReplenishment = true, QueueLimit = 0 }));
    options.AddPolicy("autocomplete", context => RateLimitPartition.GetSlidingWindowLimiter(ClientKey(context), _ => new SlidingWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1), SegmentsPerWindow = 6, AutoReplenishment = true, QueueLimit = 0 }));
    options.AddPolicy("cart", context => RateLimitPartition.GetTokenBucketLimiter(ClientKey(context), _ => new TokenBucketRateLimiterOptions { TokenLimit = 40, TokensPerPeriod = 40, ReplenishmentPeriod = TimeSpan.FromMinutes(1), AutoReplenishment = true, QueueLimit = 0 }));
    options.AddPolicy("checkout", context => RateLimitPartition.GetFixedWindowLimiter(ClientKey(context), _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(1), AutoReplenishment = true, QueueLimit = 0 }));
});
builder.Services.AddCors(options => options.AddPolicy("frontend", policy => policy.WithOrigins(builder.Configuration["FRONTEND_ORIGIN"] ?? "http://localhost:3000").AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

var authority = builder.Configuration["AUTH_AUTHORITY"];
if (!string.IsNullOrWhiteSpace(authority))
{
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
    {
        options.Authority = authority;
        options.Audience = builder.Configuration["AUTH_AUDIENCE"] ?? "commerce-api";
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
    });
    builder.Services.AddAuthorization();
}

var app = builder.Build();
app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    if (exception is OperationCanceledException && context.RequestAborted.IsCancellationRequested) return;
    var known = exception as CommerceException;
    var malformedRequest = exception is BadHttpRequestException;
    var status = known?.StatusCode ?? (malformedRequest ? StatusCodes.Status400BadRequest : StatusCodes.Status500InternalServerError);
    var code = known?.Code ?? (malformedRequest ? "malformed_request" : "internal_error");
    context.Response.StatusCode = status;
    await Results.Problem(type: $"https://commerce.example/problems/{code.Replace('_', '-')}", title: status == 500 ? "An unexpected error occurred" : "Request could not be completed", statusCode: status, detail: known?.Message, extensions: new Dictionary<string, object?> { ["code"] = code, ["traceId"] = Activity.Current?.Id ?? context.TraceIdentifier }).ExecuteAsync(context);
}));
if (!app.Environment.IsDevelopment()) { app.UseHsts(); app.UseHttpsRedirection(); }
app.Use(async (context, next) =>
{
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers.XFrameOptions = "DENY";
    context.Response.Headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";
    if (HttpMethods.IsGet(context.Request.Method) &&
        (context.Request.Path.StartsWithSegments("/api/catalog") || context.Request.Path.StartsWithSegments("/api/storefront")) &&
        !context.Request.Path.StartsWithSegments("/api/storefront/cart-summary"))
        context.Response.Headers.CacheControl = "public,max-age=30,stale-while-revalidate=60";
    await next();
});
app.UseResponseCompression();
app.UseCors("frontend");
app.UseRateLimiter();
app.UseRequestTimeouts();
app.UseOutputCache();
if (!string.IsNullOrWhiteSpace(authority)) { app.UseAuthentication(); app.UseAuthorization(); }

app.MapOpenApi();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

var api = app.MapGroup("/api");
api.MapGet("/catalog/categories", (CatalogService service, CancellationToken ct) => service.GetCategoriesAsync(ct)).CacheOutput("public-short").RequireRateLimiting("catalog").WithRequestTimeout("catalog");
api.MapGet("/catalog/products", (string? q, string? category, string? brand, decimal? minPrice, decimal? maxPrice, decimal? minimumRating, bool? available, string? sort, int? page, int? pageSize, CatalogService service, CancellationToken ct) => service.SearchAsync(new SearchRequest(q, category, brand, minPrice, maxPrice, minimumRating, available, sort, page ?? 1, pageSize ?? 24), ct)).RequireRateLimiting("catalog").WithRequestTimeout("search");
api.MapGet("/catalog/products/{slug}", (string slug, CatalogService service, CancellationToken ct) => service.GetProductAsync(slug, ct)).CacheOutput("public-product").RequireRateLimiting("catalog").WithRequestTimeout("catalog");
api.MapPost("/catalog/products/batch", (BatchProductsRequest request, CatalogService service, CancellationToken ct) => service.GetBatchAsync(request.ProductIds, ct)).RequireRateLimiting("catalog").WithRequestTimeout("catalog");
api.MapGet("/search/suggestions", (string q, CatalogService service, CancellationToken ct) => service.SuggestAsync(q, ct)).RequireRateLimiting("autocomplete").WithRequestTimeout("autocomplete");
api.MapGet("/storefront/home", (StorefrontService service, CancellationToken ct) => service.GetHomeAsync(ct)).CacheOutput("public-short").RequireRateLimiting("catalog").WithRequestTimeout("catalog");
api.MapGet("/storefront/products/{slug}", (string slug, StorefrontService service, CancellationToken ct) => service.GetProductAsync(slug, ct)).CacheOutput("public-product").RequireRateLimiting("catalog").WithRequestTimeout("catalog");

api.MapGet("/storefront/cart-summary", async (HttpContext http, CartService service, CancellationToken ct) => { NoStore(http); return await service.GetSummaryAsync(CustomerId(http, app.Environment), ct); }).RequireRateLimiting("cart");
api.MapGet("/cart", async (HttpContext http, CartService service, CancellationToken ct) => { NoStore(http); return await service.GetAsync(CustomerId(http, app.Environment), ct); }).RequireRateLimiting("cart");
api.MapPut("/cart/items", async (HttpContext http, SetCartItemRequest request, CartService service, CancellationToken ct) => { NoStore(http); return await service.SetItemAsync(CustomerId(http, app.Environment), request, ct); }).RequireRateLimiting("cart");
api.MapDelete("/cart/items/{variantId:guid}", async (HttpContext http, Guid variantId, CartService service, CancellationToken ct) => { NoStore(http); return await service.RemoveItemAsync(CustomerId(http, app.Environment), variantId, ct); }).RequireRateLimiting("cart");
api.MapDelete("/cart", async (HttpContext http, CartService service, CancellationToken ct) => { NoStore(http); return await service.ClearAsync(CustomerId(http, app.Environment), ct); }).RequireRateLimiting("cart");
api.MapPost("/checkout/confirm", async (HttpContext http, CheckoutRequest request, CheckoutService service, CancellationToken ct) =>
{
    NoStore(http);
    var result = await service.ConfirmAsync(CustomerId(http, app.Environment), http.Request.Headers["Idempotency-Key"].ToString(), request, ct);
    if (result.IdempotencyReplayed) http.Response.Headers["Idempotency-Replayed"] = "true";
    return Results.Created($"/api/orders/{result.Order.Id}", result);
}).RequireRateLimiting("checkout").WithRequestTimeout("checkout");
api.MapGet("/orders", async (HttpContext http, int? pageSize, CheckoutService service, CancellationToken ct) => { NoStore(http); return await service.GetOrdersAsync(CustomerId(http, app.Environment), pageSize ?? 20, ct); }).RequireRateLimiting("cart");
api.MapGet("/orders/{id:guid}", async (HttpContext http, Guid id, CheckoutService service, CancellationToken ct) => { NoStore(http); return await service.GetOrderAsync(CustomerId(http, app.Environment), id, ct); }).RequireRateLimiting("cart");

if (app.Environment.IsDevelopment() && app.Configuration.GetValue("APPLY_MIGRATIONS", false))
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<CommerceDbContext>();
    await db.Database.MigrateAsync();
    await CommerceSeeder.SeedAsync(db, CancellationToken.None);
}

await app.RunAsync();

static string ClientKey(HttpContext context) => context.User.FindFirstValue("sub") ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
static void NoStore(HttpContext context) => context.Response.Headers.CacheControl = "no-store";
static string CustomerId(HttpContext context, IWebHostEnvironment environment)
{
    var subject = context.User.FindFirstValue("sub");
    if (!string.IsNullOrWhiteSpace(subject)) return subject;
    if (environment.IsDevelopment() && context.Request.Headers.TryGetValue("X-Customer-Id", out var values) && !string.IsNullOrWhiteSpace(values)) return values.ToString()[..Math.Min(values.ToString().Length, 200)];
    throw new CommerceException("authentication_required", "Authentication is required.", 401);
}

public partial class Program;
