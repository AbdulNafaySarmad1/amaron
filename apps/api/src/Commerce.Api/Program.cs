using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.IO.Compression;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.RateLimiting;
using System.Net;
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
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddEnvironmentVariables();
var allowDevelopmentIdentity = builder.Environment.IsDevelopment() && builder.Configuration.GetValue("ALLOW_DEVELOPMENT_IDENTITY", false);
var trustedProxies = (builder.Configuration["TRUSTED_PROXY_IPS"] ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(IPAddress.Parse).ToArray();
if (trustedProxies.Length > 0)
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
        foreach (var proxy in trustedProxies) options.KnownProxies.Add(proxy);
    });
}
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 1024 * 1024);
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
builder.Services.AddScoped<AdminCatalogService>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHealthChecks().AddDbContextCheck<CommerceDbContext>("postgresql", tags: ["ready"]);
var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("Commerce.Api"))
    .WithTracing(tracing =>
    {
        tracing.AddSource(CommerceTelemetry.Name).AddAspNetCoreInstrumentation(options => options.Filter = context => !context.Request.Path.StartsWithSegments("/health")).AddHttpClientInstrumentation();
        if (!string.IsNullOrWhiteSpace(otlpEndpoint)) tracing.AddOtlpExporter();
    })
    .WithMetrics(metrics =>
    {
        metrics.AddMeter(CommerceTelemetry.Name).AddAspNetCoreInstrumentation().AddRuntimeInstrumentation();
        if (!string.IsNullOrWhiteSpace(otlpEndpoint)) metrics.AddOtlpExporter();
    });
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
    options.AddPolicy("private-read", TimeSpan.FromSeconds(8));
    options.AddPolicy("cart-write", TimeSpan.FromSeconds(10));
    options.AddPolicy("checkout", TimeSpan.FromSeconds(20));
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, token) =>
    {
        CommerceTelemetry.RateLimitRejections.Add(1);
        context.HttpContext.Response.ContentType = "application/problem+json";
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry)) context.HttpContext.Response.Headers.RetryAfter = Math.Ceiling(retry.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
        await context.HttpContext.Response.WriteAsJsonAsync(new ProblemDetails { Type = "https://commerce.example/problems/rate-limit", Title = "Rate limit exceeded", Status = 429, Extensions = { ["code"] = "rate_limit_exceeded", ["traceId"] = Activity.Current?.Id ?? context.HttpContext.TraceIdentifier } }, token);
    };
    options.AddPolicy("catalog", context => RateLimitPartition.GetTokenBucketLimiter(ClientKey(context), _ => new TokenBucketRateLimiterOptions { TokenLimit = 120, TokensPerPeriod = 120, ReplenishmentPeriod = TimeSpan.FromMinutes(1), AutoReplenishment = true, QueueLimit = 0 }));
    options.AddPolicy("autocomplete", context => RateLimitPartition.GetSlidingWindowLimiter(ClientKey(context), _ => new SlidingWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1), SegmentsPerWindow = 6, AutoReplenishment = true, QueueLimit = 0 }));
    options.AddPolicy("cart", context => RateLimitPartition.GetTokenBucketLimiter(ClientKey(context), _ => new TokenBucketRateLimiterOptions { TokenLimit = 40, TokensPerPeriod = 40, ReplenishmentPeriod = TimeSpan.FromMinutes(1), AutoReplenishment = true, QueueLimit = 0 }));
    options.AddPolicy("checkout", context => RateLimitPartition.GetFixedWindowLimiter(ClientKey(context), _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(1), AutoReplenishment = true, QueueLimit = 0 }));
    options.AddPolicy("admin", context => RateLimitPartition.GetFixedWindowLimiter(ClientKey(context), _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), AutoReplenishment = true, QueueLimit = 0 }));
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
if (trustedProxies.Length > 0) app.UseForwardedHeaders();
app.Use(async (context, next) =>
{
    try { await next(); }
    catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
    catch (Exception exception)
    {
        if (context.Response.HasStarted) throw;
        var known = exception as CommerceException;
        var malformedRequest = exception is BadHttpRequestException;
        var databaseUnavailable = IsDatabaseUnavailable(exception);
        var status = known?.StatusCode ?? (malformedRequest ? StatusCodes.Status400BadRequest : databaseUnavailable ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status500InternalServerError);
        var code = known?.Code ?? (malformedRequest ? "malformed_request" : databaseUnavailable ? "database_unavailable" : "internal_error");
        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Commerce.Api.Errors");
        if (databaseUnavailable) logger.LogWarning("PostgreSQL unavailable while serving {Endpoint}", context.GetEndpoint()?.DisplayName);
        else if (status >= 500) logger.LogError(exception, "Unhandled request failure for {Endpoint}", context.GetEndpoint()?.DisplayName);
        if (databaseUnavailable) context.Response.Headers.RetryAfter = "5";
        context.Response.StatusCode = status;
        await Results.Problem(type: $"https://commerce.example/problems/{code.Replace('_', '-')}", title: status == 500 ? "An unexpected error occurred" : "Request could not be completed", statusCode: status, detail: known?.Message, extensions: new Dictionary<string, object?> { ["code"] = code, ["traceId"] = Activity.Current?.Id ?? context.TraceIdentifier }).ExecuteAsync(context);
    }
});
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
if (!string.IsNullOrWhiteSpace(authority)) { app.UseAuthentication(); app.UseAuthorization(); }
app.UseRateLimiter();
app.UseRequestTimeouts();
app.UseOutputCache();

app.MapOpenApi();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

var api = app.MapGroup("/api");
api.MapGet("/catalog/categories", async (HttpContext http, CatalogService service, CancellationToken ct) => ConditionalJson(http, await service.GetCategoriesAsync(ct))).CacheOutput("public-short").RequireRateLimiting("catalog").WithRequestTimeout("catalog");
api.MapGet("/catalog/products", (string? q, string? category, string? brand, decimal? minPrice, decimal? maxPrice, decimal? minimumRating, bool? available, string? sort, int? page, int? pageSize, CatalogService service, CancellationToken ct) => service.SearchAsync(new SearchRequest(q, category, brand, minPrice, maxPrice, minimumRating, available, sort, page ?? 1, pageSize ?? 24), ct)).RequireRateLimiting("catalog").WithRequestTimeout("search");
api.MapGet("/catalog/products/{slug}", async (HttpContext http, string slug, CatalogService service, CancellationToken ct) => ConditionalJson(http, await service.GetProductAsync(slug, ct))).RequireRateLimiting("catalog").WithRequestTimeout("catalog");
api.MapPost("/catalog/products/batch", (BatchProductsRequest request, CatalogService service, CancellationToken ct) => service.GetBatchAsync(request.ProductIds, ct)).RequireRateLimiting("catalog").WithRequestTimeout("catalog");
api.MapGet("/search/suggestions", (string q, CatalogService service, CancellationToken ct) => service.SuggestAsync(q, ct)).RequireRateLimiting("autocomplete").WithRequestTimeout("autocomplete");
api.MapGet("/storefront/home", (StorefrontService service, CancellationToken ct) => service.GetHomeAsync(ct)).RequireRateLimiting("catalog").WithRequestTimeout("catalog");
api.MapGet("/storefront/products/{slug}", async (HttpContext http, string slug, StorefrontService service, CancellationToken ct) => ConditionalJson(http, await service.GetProductAsync(slug, ct))).RequireRateLimiting("catalog").WithRequestTimeout("catalog");

api.MapGet("/storefront/cart-summary", async (HttpContext http, CartService service, CancellationToken ct) => { NoStore(http); return await service.GetSummaryAsync(CustomerId(http, allowDevelopmentIdentity), ct); }).RequireRateLimiting("cart").WithRequestTimeout("private-read");
api.MapGet("/cart", async (HttpContext http, CartService service, CancellationToken ct) => { NoStore(http); return await service.GetAsync(CustomerId(http, allowDevelopmentIdentity), ct); }).RequireRateLimiting("cart").WithRequestTimeout("private-read");
api.MapPut("/cart/items", async (HttpContext http, SetCartItemRequest request, CartService service, CancellationToken ct) => { NoStore(http); return await service.SetItemAsync(CustomerId(http, allowDevelopmentIdentity), request, ct); }).RequireRateLimiting("cart").WithRequestTimeout("cart-write");
api.MapDelete("/cart/items/{variantId:guid}", async (HttpContext http, Guid variantId, CartService service, CancellationToken ct) => { NoStore(http); return await service.RemoveItemAsync(CustomerId(http, allowDevelopmentIdentity), variantId, ct); }).RequireRateLimiting("cart").WithRequestTimeout("cart-write");
api.MapDelete("/cart", async (HttpContext http, CartService service, CancellationToken ct) => { NoStore(http); return await service.ClearAsync(CustomerId(http, allowDevelopmentIdentity), ct); }).RequireRateLimiting("cart").WithRequestTimeout("cart-write");
api.MapPost("/checkout/confirm", async (HttpContext http, CheckoutRequest request, CheckoutService service, CancellationToken ct) =>
{
    NoStore(http);
    var started = Stopwatch.GetTimestamp();
    using var activity = CommerceTelemetry.ActivitySource.StartActivity("checkout.confirm");
    try
    {
        var result = await service.ConfirmAsync(CustomerId(http, allowDevelopmentIdentity), http.Request.Headers["Idempotency-Key"].ToString(), request, ct);
        if (result.IdempotencyReplayed) http.Response.Headers["Idempotency-Replayed"] = "true";
        else CommerceTelemetry.OrdersCreated.Add(1);
        activity?.SetTag("commerce.checkout.replayed", result.IdempotencyReplayed);
        return Results.Created($"/api/orders/{result.Order.Id}", result);
    }
    catch
    {
        CommerceTelemetry.CheckoutFailures.Add(1);
        activity?.SetStatus(ActivityStatusCode.Error);
        throw;
    }
    finally
    {
        CommerceTelemetry.CheckoutDuration.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
    }
}).RequireRateLimiting("checkout").WithRequestTimeout("checkout");
api.MapGet("/orders", async (HttpContext http, int? pageSize, CheckoutService service, CancellationToken ct) => { NoStore(http); return await service.GetOrdersAsync(CustomerId(http, allowDevelopmentIdentity), pageSize ?? 20, ct); }).RequireRateLimiting("cart").WithRequestTimeout("private-read");
api.MapGet("/orders/{id:guid}", async (HttpContext http, Guid id, CheckoutService service, CancellationToken ct) => { NoStore(http); return await service.GetOrderAsync(CustomerId(http, allowDevelopmentIdentity), id, ct); }).RequireRateLimiting("cart").WithRequestTimeout("private-read");

var admin = api.MapGroup("/admin");
admin.RequireRateLimiting("admin").WithRequestTimeout("catalog");
admin.MapGet("/products/{id:guid}", async (HttpContext http, Guid id, AdminCatalogService service, CancellationToken ct) =>
{
    EnsureAdmin(http, allowDevelopmentIdentity);
    var product = await service.GetProductAsync(id, ct);
    http.Response.Headers.ETag = VersionEtag(product.Version);
    return Results.Ok(product);
});
admin.MapPut("/products/{id:guid}", async (HttpContext http, Guid id, UpdateProductRequest request, AdminCatalogService service, IOutputCacheStore outputCache, CancellationToken ct) =>
{
    EnsureAdmin(http, allowDevelopmentIdentity);
    var product = await service.UpdateProductAsync(id, RequiredVersion(http), request, ct);
    await EvictPublicOutputAsync(outputCache, ct);
    http.Response.Headers.ETag = VersionEtag(product.Version);
    return Results.Ok(product);
});
admin.MapGet("/inventory/{variantId:guid}", async (HttpContext http, Guid variantId, AdminCatalogService service, CancellationToken ct) =>
{
    EnsureAdmin(http, allowDevelopmentIdentity);
    var inventory = await service.GetInventoryAsync(variantId, ct);
    http.Response.Headers.ETag = VersionEtag(inventory.Version);
    return Results.Ok(inventory);
});
admin.MapPut("/inventory/{variantId:guid}", async (HttpContext http, Guid variantId, UpdateInventoryRequest request, AdminCatalogService service, IOutputCacheStore outputCache, CancellationToken ct) =>
{
    EnsureAdmin(http, allowDevelopmentIdentity);
    var inventory = await service.UpdateInventoryAsync(variantId, RequiredVersion(http), request, ct);
    await EvictPublicOutputAsync(outputCache, ct);
    http.Response.Headers.ETag = VersionEtag(inventory.Version);
    return Results.Ok(inventory);
});

if (app.Environment.IsDevelopment() && app.Configuration.GetValue("APPLY_MIGRATIONS", false))
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<CommerceDbContext>();
    await db.Database.MigrateAsync();
    await CommerceSeeder.SeedAsync(db, CancellationToken.None);
}

await app.RunAsync();

static string ClientKey(HttpContext context)
{
    var subject = context.User.FindFirstValue("sub");
    if (!string.IsNullOrWhiteSpace(subject)) return subject[..Math.Min(subject.Length, 200)];
    var environment = context.RequestServices.GetRequiredService<IWebHostEnvironment>();
    var configuration = context.RequestServices.GetRequiredService<IConfiguration>();
    if (environment.IsDevelopment() && configuration.GetValue("ALLOW_DEVELOPMENT_IDENTITY", false) && context.Request.Headers.TryGetValue("X-Customer-Id", out var customer) && !string.IsNullOrWhiteSpace(customer)) return customer.ToString()[..Math.Min(customer.ToString().Length, 200)];
    return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
static bool IsDatabaseUnavailable(Exception? exception)
{
    for (var current = exception; current is not null; current = current.InnerException)
        if (current is NpgsqlException npgsql && npgsql.IsTransient) return true;
    return false;
}

static void NoStore(HttpContext context) => context.Response.Headers.CacheControl = "no-store";
static IResult ConditionalJson<T>(HttpContext context, T value)
{
    var etag = $"\"{Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)))}\"";
    context.Response.Headers.ETag = etag;
    return context.Request.Headers["If-None-Match"].Any(x => string.Equals(x, etag, StringComparison.Ordinal)) ? Results.StatusCode(StatusCodes.Status304NotModified) : Results.Ok(value);
}

static uint RequiredVersion(HttpContext context)
{
    var raw = context.Request.Headers["If-Match"].ToString().Trim().Trim('"');
    if (string.IsNullOrWhiteSpace(raw)) throw new CommerceException("precondition_required", "Send the current ETag in the If-Match header.", StatusCodes.Status428PreconditionRequired);
    return uint.TryParse(raw, out var version) ? version : throw CommerceErrors.Validation("If-Match must contain a valid resource version.");
}

static string VersionEtag(string version) => $"\"{version}\"";
static async Task EvictPublicOutputAsync(IOutputCacheStore outputCache, CancellationToken cancellationToken)
{
    await outputCache.EvictByTagAsync("public-products", cancellationToken);
    await outputCache.EvictByTagAsync("public-storefront", cancellationToken);
}

static void EnsureAdmin(HttpContext context, bool allowDevelopmentIdentity)
{
    var scopes = context.User.FindFirstValue("scope")?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [];
    if (context.User.IsInRole("admin") || scopes.Contains("catalog.write", StringComparer.Ordinal)) return;
    if (allowDevelopmentIdentity && string.Equals(context.Request.Headers["X-Admin"], "true", StringComparison.OrdinalIgnoreCase)) return;
    throw new CommerceException("forbidden", "Catalog administrator access is required.", 403);
}

static string CustomerId(HttpContext context, bool allowDevelopmentIdentity)
{
    var subject = context.User.FindFirstValue("sub");
    if (!string.IsNullOrWhiteSpace(subject))
    {
        if (subject.Length > 200) throw new CommerceException("invalid_identity", "The authenticated subject exceeds the supported identity length.", 401);
        return subject;
    }
    if (allowDevelopmentIdentity && context.Request.Headers.TryGetValue("X-Customer-Id", out var values) && !string.IsNullOrWhiteSpace(values)) return values.ToString()[..Math.Min(values.ToString().Length, 200)];
    throw new CommerceException("authentication_required", "Authentication is required.", 401);
}

public partial class Program;
