using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.IO.Compression;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.RateLimiting;
using System.Net;
using Commerce.Application;
using Commerce.Api;
using Commerce.Api.Identity;
using Commerce.Contracts;
using Commerce.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Npgsql;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddEnvironmentVariables();
var authority = builder.Configuration["AUTH_AUTHORITY"]?.TrimEnd('/');
var audience = builder.Configuration["AUTH_AUDIENCE"] ?? "commerce-api";
if (!builder.Environment.IsDevelopment() && string.IsNullOrWhiteSpace(authority))
    throw new InvalidOperationException("AUTH_AUTHORITY is required outside Development.");
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
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "OIDC access token issued for the commerce-api audience."
        };
        return Task.CompletedTask;
    });
    options.AddOperationTransformer((operation, context, _) =>
    {
        var metadata = context.Description.ActionDescriptor.EndpointMetadata;
        if (metadata.OfType<IAuthorizeData>().Any() && !metadata.OfType<IAllowAnonymous>().Any())
        {
            operation.Security ??= [];
            operation.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("Bearer", context.Document, null)] = []
            });
        }
        return Task.CompletedTask;
    });
});
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient("identity-health", client => client.Timeout = TimeSpan.FromSeconds(3));
builder.Services.AddCommerceInfrastructure(builder.Configuration);
builder.Services.AddScoped<CatalogService>();
builder.Services.AddScoped<CartService>();
builder.Services.AddScoped<CheckoutService>();
builder.Services.AddScoped<StorefrontService>();
builder.Services.AddScoped<AdminCatalogService>();
builder.Services.AddScoped<OperationsService>();
builder.Services.AddScoped<SupplyChainService>();
builder.Services.AddScoped<WarehouseExecutionService>();
builder.Services.AddScoped<PaymentOrchestrator>();
builder.Services.AddSingleton<IPaymentProvider>(_ => new TestPaymentProvider(builder.Configuration["PAYMENT_TEST_WEBHOOK_SECRET"] ?? "development-test-webhook-secret"));
builder.Services.AddSingleton<IInvoiceResolver, GenericQrInvoiceResolver>();
builder.Services.AddSingleton<IInvoiceResolver, FbrInvoiceResolver>();
builder.Services.AddSingleton<IInvoiceResolver, SrbInvoiceResolver>();
builder.Services.AddHostedService<OperationsPriceActivationService>();
builder.Services.AddHostedService<PaymentExpiryService>();
builder.Services.AddScoped<ApplicationUserResolver>();
builder.Services.AddTransient<IClaimsTransformation, KeycloakClaimsTransformation>();
builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, SecurityAuthorizationResultHandler>();
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
var frontendOrigins = (builder.Configuration["FRONTEND_ORIGINS"] ?? builder.Configuration["FRONTEND_ORIGIN"] ?? "http://localhost:3000")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
builder.Services.AddCors(options => options.AddPolicy("frontend", policy => policy.WithOrigins(frontendOrigins).WithHeaders("Accept", "Content-Type", "Authorization", "Idempotency-Key", "If-Match", "If-None-Match").WithMethods("GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS")));

var authentication = builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = "CommerceIdentity";
    options.DefaultChallengeScheme = "CommerceIdentity";
    options.DefaultForbidScheme = "CommerceIdentity";
}).AddPolicyScheme("CommerceIdentity", "Bearer or development identity", options =>
{
    options.ForwardDefaultSelector = context =>
        !string.IsNullOrWhiteSpace(authority) && context.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? JwtBearerDefaults.AuthenticationScheme
            : DevelopmentIdentityDefaults.Scheme;
}).AddScheme<AuthenticationSchemeOptions, DevelopmentIdentityHandler>(DevelopmentIdentityDefaults.Scheme, _ => { });

if (!string.IsNullOrWhiteSpace(authority))
{
    authentication.AddJwtBearer(options =>
    {
        options.Authority = authority;
        if (builder.Configuration["AUTH_METADATA_ADDRESS"] is { Length: > 0 } metadataAddress) options.MetadataAddress = metadataAddress;
        options.Audience = audience;
        options.MapInboundClaims = false;
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
        options.RefreshOnIssuerKeyNotFound = true;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = authority,
            ValidateAudience = true,
            ValidAudience = audience,
            ValidateIssuerSigningKey = true,
            RequireSignedTokens = true,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            NameClaimType = "name",
            ValidTypes = ["JWT", "at+jwt"]
        };
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = context =>
            {
                var subject = context.Principal?.FindFirstValue("sub");
                var tokenType = context.Principal?.FindFirstValue("typ");
                if (string.IsNullOrWhiteSpace(subject) || (!string.IsNullOrWhiteSpace(tokenType) && !string.Equals(tokenType, "Bearer", StringComparison.OrdinalIgnoreCase)))
                    context.Fail("The access token is missing required claims.");
                return Task.CompletedTask;
            },
            OnAuthenticationFailed = context =>
            {
                CommerceTelemetry.AuthenticationFailures.Add(1);
                context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Commerce.Security").LogWarning("Access token validation failed: {FailureType}", context.Exception.GetType().Name);
                return Task.CompletedTask;
            }
        };
    });
}
builder.Services.AddAuthorization(options => options.AddCommercePolicies());

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
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
app.UseRequestTimeouts();
app.UseOutputCache();

if (app.Environment.IsDevelopment()) app.MapOpenApi().AllowAnonymous();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") }).AllowAnonymous();
app.MapGet("/health/identity", async (IHttpClientFactory clients, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(authority)) return Results.Json(new { available = false }, statusCode: StatusCodes.Status503ServiceUnavailable);
    var metadataAddress = builder.Configuration["AUTH_METADATA_ADDRESS"] ?? $"{authority}/.well-known/openid-configuration";
    try
    {
        using var response = await clients.CreateClient("identity-health").GetAsync(metadataAddress, ct);
        return response.IsSuccessStatusCode ? Results.Ok(new { available = true }) : Results.Json(new { available = false }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (HttpRequestException) { return Results.Json(new { available = false }, statusCode: StatusCodes.Status503ServiceUnavailable); }
    catch (TaskCanceledException) when (!ct.IsCancellationRequested) { return Results.Json(new { available = false }, statusCode: StatusCodes.Status503ServiceUnavailable); }
}).RequireAuthorization(CommercePolicies.AdministrationAccess);

var api = app.MapGroup("/api");
api.MapPost("/payments/webhooks/{provider}", async (HttpContext http, string provider, PaymentOrchestrator service, CancellationToken ct) =>
{
    using var reader = new StreamReader(http.Request.Body, leaveOpen: false);
    var payload = await reader.ReadToEndAsync(ct);
    await service.HandleWebhookAsync(provider, payload, http.Request.Headers["X-Payment-Signature"].ToString(), ct);
    return Results.Accepted();
}).AllowAnonymous().RequireRateLimiting("admin").WithRequestTimeout("cart-write");
api.MapGet("/catalog/categories", async (HttpContext http, CatalogService service, CancellationToken ct) => ConditionalJson(http, await service.GetCategoriesAsync(ct))).CacheOutput("public-short").RequireRateLimiting("catalog").WithRequestTimeout("catalog").AllowAnonymous();
api.MapGet("/catalog/products", (string? q, string? category, string? brand, decimal? minPrice, decimal? maxPrice, decimal? minimumRating, bool? available, string? sort, int? page, int? pageSize, CatalogService service, CancellationToken ct) => service.SearchAsync(new SearchRequest(q, category, brand, minPrice, maxPrice, minimumRating, available, sort, page ?? 1, pageSize ?? 24), ct)).RequireRateLimiting("catalog").WithRequestTimeout("search").AllowAnonymous();
api.MapGet("/catalog/products/{slug}", async (HttpContext http, string slug, CatalogService service, CancellationToken ct) => ConditionalJson(http, await service.GetProductAsync(slug, ct))).RequireRateLimiting("catalog").WithRequestTimeout("catalog").AllowAnonymous();
api.MapPost("/catalog/products/batch", (BatchProductsRequest request, CatalogService service, CancellationToken ct) => service.GetBatchAsync(request.ProductIds, ct)).RequireRateLimiting("catalog").WithRequestTimeout("catalog").AllowAnonymous();
api.MapGet("/search/suggestions", (string q, CatalogService service, CancellationToken ct) => service.SuggestAsync(q, ct)).RequireRateLimiting("autocomplete").WithRequestTimeout("autocomplete").AllowAnonymous();
api.MapGet("/storefront/home", (StorefrontService service, CancellationToken ct) => service.GetHomeAsync(ct)).RequireRateLimiting("catalog").WithRequestTimeout("catalog").AllowAnonymous();
api.MapGet("/storefront/products/{slug}", async (HttpContext http, string slug, StorefrontService service, CancellationToken ct) => ConditionalJson(http, await service.GetProductAsync(slug, ct))).RequireRateLimiting("catalog").WithRequestTimeout("catalog").AllowAnonymous();

var customer = api.MapGroup("").RequireAuthorization(CommercePolicies.CustomerAccess);
customer.MapGet("/storefront/cart-summary", async (HttpContext http, ApplicationUserResolver user, CartService service, CancellationToken ct) => { NoStore(http); return await service.GetSummaryAsync(await user.GetRequiredUserIdAsync(ct), ct); }).RequireRateLimiting("cart").WithRequestTimeout("private-read");
customer.MapGet("/cart", async (HttpContext http, ApplicationUserResolver user, CartService service, CancellationToken ct) => { NoStore(http); return await service.GetAsync(await user.GetRequiredUserIdAsync(ct), ct); }).RequireRateLimiting("cart").WithRequestTimeout("private-read");
customer.MapPut("/cart/items", async (HttpContext http, SetCartItemRequest request, ApplicationUserResolver user, CartService service, CancellationToken ct) => { NoStore(http); return await service.SetItemAsync(await user.GetRequiredUserIdAsync(ct), request, ct); }).RequireRateLimiting("cart").WithRequestTimeout("cart-write");
customer.MapDelete("/cart/items/{variantId:guid}", async (HttpContext http, Guid variantId, ApplicationUserResolver user, CartService service, CancellationToken ct) => { NoStore(http); return await service.RemoveItemAsync(await user.GetRequiredUserIdAsync(ct), variantId, ct); }).RequireRateLimiting("cart").WithRequestTimeout("cart-write");
customer.MapDelete("/cart", async (HttpContext http, ApplicationUserResolver user, CartService service, CancellationToken ct) => { NoStore(http); return await service.ClearAsync(await user.GetRequiredUserIdAsync(ct), ct); }).RequireRateLimiting("cart").WithRequestTimeout("cart-write");
customer.MapPost("/checkout/confirm", async (HttpContext http, CheckoutRequest request, ApplicationUserResolver user, CheckoutService service, CancellationToken ct) =>
{
    NoStore(http);
    var started = Stopwatch.GetTimestamp();
    using var activity = CommerceTelemetry.ActivitySource.StartActivity("checkout.confirm");
    try
    {
        var result = await service.ConfirmAsync(await user.GetRequiredUserIdAsync(ct), http.Request.Headers["Idempotency-Key"].ToString(), request, ct);
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
customer.MapGet("/payments/{id:guid}", async (HttpContext http, Guid id, ApplicationUserResolver user, PaymentOrchestrator service, CancellationToken ct) => { NoStore(http); var payment = await service.GetPaymentAsync(id, ct); if (payment.CustomerId != await user.GetRequiredUserIdAsync(ct)) return Results.NotFound(); return Results.Ok(payment); }).RequireRateLimiting("cart").WithRequestTimeout("private-read");
customer.MapPost("/payments/{id:guid}/confirm", async (HttpContext http, Guid id, ConfirmPaymentRequest request, CancellationToken ct) => { var service = http.RequestServices.GetRequiredService<PaymentOrchestrator>(); var user = http.RequestServices.GetRequiredService<ApplicationUserResolver>(); var payment = await service.GetPaymentAsync(id, ct); if (payment.CustomerId != await user.GetRequiredUserIdAsync(ct)) return Results.NotFound(); return Results.Ok(await service.ConfirmAsync(id, http.Request.Headers["Idempotency-Key"].ToString(), request.PaymentMethodToken, ct)); }).RequireRateLimiting("checkout").WithRequestTimeout("checkout");
customer.MapGet("/orders", async (HttpContext http, int? pageSize, ApplicationUserResolver user, IAuthorizationService authorization, CheckoutService service, CancellationToken ct) => { NoStore(http); var canReadAny = (await authorization.AuthorizeAsync(http.User, CommercePolicies.OrdersReadAny)).Succeeded; return await service.GetOrdersAsync(await user.GetRequiredUserIdAsync(ct), pageSize ?? 20, canReadAny, ct); }).RequireAuthorization(CommercePolicies.OrdersReadOwn).RequireRateLimiting("cart").WithRequestTimeout("private-read");
customer.MapGet("/orders/{id:guid}", async (HttpContext http, Guid id, ApplicationUserResolver user, IAuthorizationService authorization, CheckoutService service, CancellationToken ct) => { NoStore(http); var canReadAny = (await authorization.AuthorizeAsync(http.User, CommercePolicies.OrdersReadAny)).Succeeded; return await service.GetOrderAsync(await user.GetRequiredUserIdAsync(ct), id, canReadAny, ct); }).RequireAuthorization(CommercePolicies.OrdersReadOwn).RequireRateLimiting("cart").WithRequestTimeout("private-read");

var admin = api.MapGroup("/admin");
admin.RequireAuthorization(CommercePolicies.AdministrationAccess).RequireRateLimiting("admin").WithRequestTimeout("catalog");
admin.MapGet("/payments", async (HttpContext http, PaymentOrchestrator service, CancellationToken ct) => { NoStore(http); return await service.GetPaymentsAsync(ct); }).RequireAuthorization(CommercePolicies.PaymentsRead);
admin.MapGet("/payments/providers", (PaymentOrchestrator service) => service.GetProviders()).RequireAuthorization(CommercePolicies.PaymentsRead);
admin.MapGet("/payments/{id:guid}", async (HttpContext http, Guid id, PaymentOrchestrator service, CancellationToken ct) => { NoStore(http); return await service.GetPaymentAsync(id, ct); }).RequireAuthorization(CommercePolicies.PaymentsRead);
admin.MapPost("/payments/{id:guid}/capture", async (Guid id, CapturePaymentRequest request, ApplicationUserResolver user, PaymentOrchestrator service, CancellationToken ct) => await service.CaptureAsync(id, request.Amount, await user.GetRequiredUserIdAsync(ct), ct)).RequireAuthorization(CommercePolicies.PaymentsCapture);
admin.MapPost("/payments/{id:guid}/refunds", async (HttpContext http, Guid id, RefundRequest request, CancellationToken ct) => { var authorization = http.RequestServices.GetRequiredService<IAuthorizationService>(); if (request.Amount > 1_000m && !(await authorization.AuthorizeAsync(http.User, CommercePolicies.PaymentsRefundLarge)).Succeeded) return Results.Forbid(); var service = http.RequestServices.GetRequiredService<PaymentOrchestrator>(); var user = http.RequestServices.GetRequiredService<ApplicationUserResolver>(); return Results.Ok(await service.RefundAsync(id, request, http.Request.Headers["Idempotency-Key"].ToString(), await user.GetRequiredUserIdAsync(ct), ct)); }).RequireAuthorization(CommercePolicies.PaymentsRefund);
admin.MapPost("/payments/{id:guid}/reconcile", async (Guid id, ApplicationUserResolver user, PaymentOrchestrator service, CancellationToken ct) => await service.ReconcileAsync(id, await user.GetRequiredUserIdAsync(ct), ct)).RequireAuthorization(CommercePolicies.PaymentsReconcile);
admin.MapGet("/products/{id:guid}", async (HttpContext http, Guid id, AdminCatalogService service, CancellationToken ct) =>
{
    var product = await service.GetProductAsync(id, ct);
    http.Response.Headers.ETag = VersionEtag(product.Version);
    return Results.Ok(product);
}).RequireAuthorization(CommercePolicies.CatalogRead);
admin.MapPut("/products/{id:guid}", async (HttpContext http, Guid id, UpdateProductRequest request, AdminCatalogService service, IOutputCacheStore outputCache, CancellationToken ct) =>
{
    var product = await service.UpdateProductAsync(id, RequiredVersion(http), request, ct);
    await EvictPublicOutputAsync(outputCache, ct);
    http.Response.Headers.ETag = VersionEtag(product.Version);
    return Results.Ok(product);
}).RequireAuthorization(CommercePolicies.CatalogManage);
admin.MapGet("/inventory/{variantId:guid}", async (HttpContext http, Guid variantId, AdminCatalogService service, CancellationToken ct) =>
{
    var inventory = await service.GetInventoryAsync(variantId, ct);
    http.Response.Headers.ETag = VersionEtag(inventory.Version);
    return Results.Ok(inventory);
}).RequireAuthorization(CommercePolicies.InventoryRead);
admin.MapPut("/inventory/{variantId:guid}", async (HttpContext http, Guid variantId, UpdateInventoryRequest request, ApplicationUserResolver user, AdminCatalogService service, IOutputCacheStore outputCache, CancellationToken ct) =>
{
    var inventory = await service.UpdateInventoryAsync(variantId, RequiredVersion(http), request, await user.GetRequiredUserIdAsync(ct), ct);
    await EvictPublicOutputAsync(outputCache, ct);
    http.Response.Headers.ETag = VersionEtag(inventory.Version);
    return Results.Ok(inventory);
}).RequireAuthorization(CommercePolicies.InventoryManage);

var operations = admin.MapGroup("/operations");
operations.MapGet("/dashboard", async (HttpContext http, OperationsService service, CancellationToken ct) => { NoStore(http); return await service.GetDashboardAsync(ct); }).RequireAuthorization(CommercePolicies.OperationsRead);
operations.MapGet("/variants", async (HttpContext http, string? query, int? page, int? pageSize, OperationsService service, CancellationToken ct) => { NoStore(http); return await service.GetVariantsAsync(query, page ?? 1, pageSize ?? 25, ct); }).RequireAuthorization(CommercePolicies.PricingRead);
operations.MapGet("/pricing/{variantId:guid}", async (HttpContext http, Guid variantId, DateTimeOffset? from, DateTimeOffset? to, OperationsService service, CancellationToken ct) => { NoStore(http); var end = to ?? DateTimeOffset.UtcNow; return await service.GetPricingCurveAsync(variantId, from ?? end.AddDays(-90), end, ct); }).RequireAuthorization(CommercePolicies.PricingRead);
operations.MapPost("/pricing/simulate", async (PriceSimulationRequest request, OperationsService service, CancellationToken ct) => await service.SimulatePriceAsync(request, ct)).RequireAuthorization(CommercePolicies.PricingRead);
operations.MapPost("/pricing/schedules", async (PriceScheduleRequest request, ApplicationUserResolver user, OperationsService service, CancellationToken ct) => Results.Created("/api/admin/operations/pricing", await service.SchedulePriceAsync(request, await user.GetRequiredUserIdAsync(ct), ct))).RequireAuthorization(CommercePolicies.PricingManage);
operations.MapGet("/pricing/recommendations", async (HttpContext http, OperationsService service, CancellationToken ct) => { NoStore(http); return await service.GetPriceRecommendationsAsync(ct); }).RequireAuthorization(CommercePolicies.PricingRead);
operations.MapPost("/pricing/{id:guid}/approve", async (Guid id, PriceApprovalRequest request, ApplicationUserResolver user, OperationsService service, CancellationToken ct) => await service.ApprovePricingAsync(id, await user.GetRequiredUserIdAsync(ct), ApprovalReason(request.Reason), ct)).RequireAuthorization(CommercePolicies.PricingApprove);
operations.MapGet("/demand", async (HttpContext http, Guid? variantId, DateTimeOffset? from, OperationsService service, CancellationToken ct) => { NoStore(http); return await service.GetDemandAsync(variantId, from ?? DateTimeOffset.UtcNow.AddDays(-90), ct); }).RequireAuthorization(CommercePolicies.DemandRead);
operations.MapPost("/forecasts/generate", async (ForecastRequest request, ApplicationUserResolver user, OperationsService service, CancellationToken ct) => Results.Created("/api/admin/operations/demand", await service.GenerateForecastAsync(request, await user.GetRequiredUserIdAsync(ct), ct))).RequireAuthorization(CommercePolicies.DemandManage);
operations.MapGet("/inventory", async (HttpContext http, OperationsService service, CancellationToken ct) => { NoStore(http); return await service.GetInventoryAsync(ct); }).RequireAuthorization(CommercePolicies.InventoryRead);
operations.MapPost("/inventory/adjustments", async (InventoryAdjustmentRequest request, ApplicationUserResolver user, OperationsService service, CancellationToken ct) => Results.Created("/api/admin/operations/inventory", await service.AdjustInventoryAsync(request, await user.GetRequiredUserIdAsync(ct), ct))).RequireAuthorization(CommercePolicies.InventoryManage);
operations.MapGet("/replenishment", async (HttpContext http, OperationsService service, CancellationToken ct) => { NoStore(http); return await service.GetReplenishmentAsync(ct); }).RequireAuthorization(CommercePolicies.ReplenishmentRead);
operations.MapPost("/replenishment/generate", async (ApplicationUserResolver user, OperationsService service, CancellationToken ct) => await service.GenerateReplenishmentAsync(await user.GetRequiredUserIdAsync(ct), ct)).RequireAuthorization(CommercePolicies.ReplenishmentManage);
operations.MapPost("/replenishment/{id:guid}/approve", async (Guid id, PriceApprovalRequest request, ApplicationUserResolver user, OperationsService service, CancellationToken ct) => await service.ApproveReplenishmentAsync(id, await user.GetRequiredUserIdAsync(ct), ApprovalReason(request.Reason), ct)).RequireAuthorization(CommercePolicies.ReplenishmentManage);
operations.MapGet("/promotions", async (HttpContext http, OperationsService service, CancellationToken ct) => { NoStore(http); return await service.GetPromotionsAsync(ct); }).RequireAuthorization(CommercePolicies.PromotionsRead);
operations.MapPost("/promotions", async (PromotionRequest request, ApplicationUserResolver user, OperationsService service, CancellationToken ct) => Results.Created("/api/admin/operations/promotions", await service.CreatePromotionAsync(request, await user.GetRequiredUserIdAsync(ct), ct))).RequireAuthorization(CommercePolicies.PromotionsManage);
operations.MapPost("/promotions/{id:guid}/approve", async (Guid id, PriceApprovalRequest request, ApplicationUserResolver user, OperationsService service, CancellationToken ct) => await service.ApprovePromotionAsync(id, await user.GetRequiredUserIdAsync(ct), ApprovalReason(request.Reason), ct)).RequireAuthorization(CommercePolicies.PromotionsManage);
operations.MapGet("/approvals", async (HttpContext http, OperationsService service, CancellationToken ct) => { NoStore(http); return await service.GetApprovalsAsync(ct); }).RequireAuthorization(CommercePolicies.OperationsRead);
operations.MapGet("/audit", async (HttpContext http, int? pageSize, OperationsService service, CancellationToken ct) => { NoStore(http); return await service.GetAuditAsync(pageSize ?? 100, ct); }).RequireAuthorization(CommercePolicies.OperationsAudit);
operations.MapGet("/alerts", async (HttpContext http, OperationsService service, CancellationToken ct) => { NoStore(http); return await service.GetAlertsAsync(ct); }).RequireAuthorization(CommercePolicies.OperationsRead);
operations.MapPost("/alerts/{id:guid}/acknowledge", async (Guid id, ApplicationUserResolver user, OperationsService service, CancellationToken ct) => await service.AcknowledgeAlertAsync(id, await user.GetRequiredUserIdAsync(ct), ct)).RequireAuthorization(CommercePolicies.OperationsRead);
operations.MapPost("/bulk/prices/preview", async (BulkPriceRequest request, ApplicationUserResolver user, OperationsService service, CancellationToken ct) => await service.BulkPricesAsync(request, await user.GetRequiredUserIdAsync(ct), false, ct)).RequireAuthorization(CommercePolicies.PricingManage);
operations.MapPost("/bulk/prices/apply", async (BulkPriceRequest request, ApplicationUserResolver user, OperationsService service, CancellationToken ct) => await service.BulkPricesAsync(request, await user.GetRequiredUserIdAsync(ct), true, ct)).RequireAuthorization(CommercePolicies.PricingManage);

var supplyChain = admin.MapGroup("/supply-chain");
supplyChain.MapGet("/suppliers", async (HttpContext http, SupplyChainService service, CancellationToken ct) => { NoStore(http); return await service.GetSuppliersAsync(ct); }).RequireAuthorization(CommercePolicies.SuppliersRead);
supplyChain.MapPost("/suppliers", async (CreateSupplierRequest request, ApplicationUserResolver user, SupplyChainService service, CancellationToken ct) => Results.Created("/api/admin/supply-chain/suppliers", await service.CreateSupplierAsync(request, await user.GetRequiredUserIdAsync(ct), ct))).RequireAuthorization(CommercePolicies.SuppliersManage);
supplyChain.MapPost("/suppliers/{supplierId:guid}/users", async (Guid supplierId, AssignSupplierUserRequest request, ApplicationUserResolver user, SupplyChainService service, CancellationToken ct) => { await service.AssignSupplierUserAsync(supplierId, request, await user.GetRequiredUserIdAsync(ct), ct); return Results.NoContent(); }).RequireAuthorization(CommercePolicies.SuppliersManage);
supplyChain.MapGet("/sources", async (HttpContext http, Guid? variantId, SupplyChainService service, CancellationToken ct) => { NoStore(http); return await service.GetSourcesAsync(variantId, ct); }).RequireAuthorization(CommercePolicies.ProcurementRead);
supplyChain.MapPost("/sources", async (SupplierSourceRequest request, ApplicationUserResolver user, SupplyChainService service, CancellationToken ct) => Results.Created("/api/admin/supply-chain/sources", await service.CreateSourceAsync(request, await user.GetRequiredUserIdAsync(ct), ct))).RequireAuthorization(CommercePolicies.ProcurementManage);
supplyChain.MapGet("/rfqs", async (HttpContext http, SupplyChainService service, CancellationToken ct) => { NoStore(http); return await service.GetRfqsAsync(null, ct); }).RequireAuthorization(CommercePolicies.ProcurementRead);
supplyChain.MapPost("/rfqs", async (CreateRfqRequest request, ApplicationUserResolver user, SupplyChainService service, CancellationToken ct) => Results.Created("/api/admin/supply-chain/rfqs", await service.CreateRfqAsync(request, await user.GetRequiredUserIdAsync(ct), ct))).RequireAuthorization(CommercePolicies.ProcurementManage);
supplyChain.MapPost("/rfqs/{id:guid}/open", async (Guid id, ApplicationUserResolver user, SupplyChainService service, CancellationToken ct) => await service.OpenRfqAsync(id, await user.GetRequiredUserIdAsync(ct), ct)).RequireAuthorization(CommercePolicies.ProcurementManage);
supplyChain.MapPost("/rfqs/{id:guid}/close", async (Guid id, ApplicationUserResolver user, SupplyChainService service, CancellationToken ct) => await service.CloseRfqAsync(id, await user.GetRequiredUserIdAsync(ct), ct)).RequireAuthorization(CommercePolicies.ProcurementManage);
supplyChain.MapGet("/rfqs/{id:guid}/quotations", async (HttpContext http, Guid id, SupplyChainService service, CancellationToken ct) => { NoStore(http); return await service.GetQuotationsAsync(id, ct); }).RequireAuthorization(CommercePolicies.ProcurementRead);
supplyChain.MapPost("/rfqs/{id:guid}/award", async (Guid id, AwardQuotationRequest request, ApplicationUserResolver user, SupplyChainService service, CancellationToken ct) => await service.AwardQuotationAsync(id, request, await user.GetRequiredUserIdAsync(ct), ct)).RequireAuthorization(CommercePolicies.ProcurementApprove);
supplyChain.MapGet("/purchase-orders", async (HttpContext http, Guid? supplierId, SupplyChainService service, CancellationToken ct) => { NoStore(http); return await service.GetPurchaseOrdersAsync(supplierId, null, ct); }).RequireAuthorization(CommercePolicies.ProcurementRead);
supplyChain.MapPost("/purchase-orders", async (CreatePurchaseOrderRequest request, ApplicationUserResolver user, SupplyChainService service, CancellationToken ct) => Results.Created("/api/admin/supply-chain/purchase-orders", await service.CreatePurchaseOrderAsync(request, await user.GetRequiredUserIdAsync(ct), ct))).RequireAuthorization(CommercePolicies.ProcurementManage);
supplyChain.MapPost("/purchase-orders/{id:guid}/approve", async (Guid id, ApplicationUserResolver user, SupplyChainService service, CancellationToken ct) => await service.ApprovePurchaseOrderAsync(id, await user.GetRequiredUserIdAsync(ct), ct)).RequireAuthorization(CommercePolicies.ProcurementApprove);
supplyChain.MapGet("/shipments", async (HttpContext http, Guid? supplierId, SupplyChainService service, CancellationToken ct) => { NoStore(http); return await service.GetInboundShipmentsAsync(supplierId, ct); }).RequireAuthorization(CommercePolicies.ProcurementRead);
supplyChain.MapGet("/invoices", async (HttpContext http, Guid? supplierId, SupplyChainService service, CancellationToken ct) => { NoStore(http); return await service.GetInvoicesAsync(supplierId, ct); }).RequireAuthorization(CommercePolicies.InvoicesRead);
supplyChain.MapPost("/invoices", async (InvoiceIngestionRequest request, ApplicationUserResolver user, SupplyChainService service, CancellationToken ct) => Results.Created("/api/admin/supply-chain/invoices", await service.IngestInvoiceAsync(request, request.SupplierOrganizationId, await user.GetRequiredUserIdAsync(ct), ct))).RequireAuthorization(CommercePolicies.InvoicesManage);
supplyChain.MapPost("/invoices/{id:guid}/match", async (Guid id, ApplicationUserResolver user, SupplyChainService service, CancellationToken ct) => await service.MatchInvoiceAsync(id, await user.GetRequiredUserIdAsync(ct), ct)).RequireAuthorization(CommercePolicies.InvoicesMatch);
supplyChain.MapGet("/receipts", async (HttpContext http, Guid? warehouseId, SupplyChainService service, CancellationToken ct) => { NoStore(http); return await service.GetReceiptsAsync(warehouseId, ct); }).RequireAuthorization(CommercePolicies.ReceivingRead);
supplyChain.MapPost("/receipts", async (HttpContext http, PostGoodsReceiptRequest request, ApplicationUserResolver user, SupplyChainService service, CancellationToken ct) => Results.Created("/api/admin/supply-chain/receipts", await service.PostReceiptAsync(request, http.Request.Headers["Idempotency-Key"].ToString(), await user.GetRequiredUserIdAsync(ct), ct))).RequireAuthorization(CommercePolicies.ReceivingManage);
supplyChain.MapGet("/inventory-balances", async (HttpContext http, Guid? warehouseId, Guid? variantId, SupplyChainService service, CancellationToken ct) => { NoStore(http); return await service.GetBalancesAsync(warehouseId, variantId, ct); }).RequireAuthorization(CommercePolicies.ReceivingRead);
supplyChain.MapGet("/warehouse-tasks", async (HttpContext http, Guid? warehouseId, WarehouseExecutionService service, CancellationToken ct) => { NoStore(http); return await service.GetTasksAsync(warehouseId, ct); }).RequireAuthorization(CommercePolicies.WarehouseTasksRead);
supplyChain.MapGet("/cycle-counts", async (HttpContext http, Guid? warehouseId, WarehouseExecutionService service, CancellationToken ct) => { NoStore(http); return await service.GetCycleCountsAsync(warehouseId, ct); }).RequireAuthorization(CommercePolicies.CycleCountsRead);
supplyChain.MapPost("/cycle-counts", async (CreateCycleCountRequest request, ApplicationUserResolver user, WarehouseExecutionService service, CancellationToken ct) => Results.Created("/api/admin/supply-chain/cycle-counts", await service.CreateCycleCountAsync(request, await user.GetRequiredUserIdAsync(ct), ct))).RequireAuthorization(CommercePolicies.CycleCountsManage);
supplyChain.MapPost("/cycle-counts/{id:guid}/start", async (Guid id, ApplicationUserResolver user, WarehouseExecutionService service, CancellationToken ct) => await service.StartCycleCountAsync(id, await user.GetRequiredUserIdAsync(ct), ct)).RequireAuthorization(CommercePolicies.CycleCountsManage);
supplyChain.MapPost("/cycle-counts/{id:guid}/submit", async (Guid id, SubmitCycleCountRequest request, ApplicationUserResolver user, WarehouseExecutionService service, CancellationToken ct) => await service.SubmitCycleCountAsync(id, request, await user.GetRequiredUserIdAsync(ct), ct)).RequireAuthorization(CommercePolicies.CycleCountsManage);
supplyChain.MapPost("/cycle-counts/{id:guid}/reconcile", async (Guid id, ApplicationUserResolver user, WarehouseExecutionService service, CancellationToken ct) => await service.ReconcileCycleCountAsync(id, await user.GetRequiredUserIdAsync(ct), ct)).RequireAuthorization(CommercePolicies.CycleCountsReconcile);
supplyChain.MapGet("/stock-transfers", async (HttpContext http, Guid? warehouseId, WarehouseExecutionService service, CancellationToken ct) => { NoStore(http); return await service.GetStockTransfersAsync(warehouseId, ct); }).RequireAuthorization(CommercePolicies.StockTransfersRead);
supplyChain.MapPost("/stock-transfers", async (HttpContext http, CreateStockTransferRequest request, ApplicationUserResolver user, WarehouseExecutionService service, CancellationToken ct) => Results.Created("/api/admin/supply-chain/stock-transfers", await service.CreateStockTransferAsync(request, http.Request.Headers["Idempotency-Key"].ToString(), await user.GetRequiredUserIdAsync(ct), ct))).RequireAuthorization(CommercePolicies.StockTransfersManage);
supplyChain.MapPost("/stock-transfers/{id:guid}/dispatch", async (HttpContext http, Guid id, ApplicationUserResolver user, WarehouseExecutionService service, CancellationToken ct) => await service.DispatchStockTransferAsync(id, http.Request.Headers["Idempotency-Key"].ToString(), await user.GetRequiredUserIdAsync(ct), ct)).RequireAuthorization(CommercePolicies.StockTransfersManage);
supplyChain.MapPost("/stock-transfers/{id:guid}/receive", async (HttpContext http, Guid id, ApplicationUserResolver user, WarehouseExecutionService service, CancellationToken ct) => await service.ReceiveStockTransferAsync(id, http.Request.Headers["Idempotency-Key"].ToString(), await user.GetRequiredUserIdAsync(ct), ct)).RequireAuthorization(CommercePolicies.StockTransfersManage);

var supplier = api.MapGroup("/supplier");
supplier.RequireAuthorization(CommercePolicies.SupplierPortal).RequireRateLimiting("admin").WithRequestTimeout("catalog");
supplier.MapGet("/purchase-orders", async (HttpContext http, ApplicationUserResolver user, SupplyChainService service, CancellationToken ct) => { NoStore(http); var actor = await user.GetRequiredUserIdAsync(ct); return await service.GetPurchaseOrdersAsync(null, await service.GetSupplierScopeAsync(actor, ct), ct); });
supplier.MapGet("/rfqs", async (HttpContext http, ApplicationUserResolver user, SupplyChainService service, CancellationToken ct) => { NoStore(http); var actor = await user.GetRequiredUserIdAsync(ct); return await service.GetRfqsAsync(await service.GetSupplierScopeAsync(actor, ct), ct); });
supplier.MapPost("/rfqs/{id:guid}/quotations", async (Guid id, SubmitQuotationRequest request, ApplicationUserResolver user, SupplyChainService service, CancellationToken ct) => { var actor = await user.GetRequiredUserIdAsync(ct); return Results.Created($"/api/supplier/rfqs/{id}/quotations", await service.SubmitQuotationAsync(id, request, await service.GetSupplierScopeAsync(actor, ct), actor, ct)); });
supplier.MapPost("/shipments", async (HttpContext http, CreateInboundShipmentRequest request, ApplicationUserResolver user, SupplyChainService service, CancellationToken ct) => { var actor = await user.GetRequiredUserIdAsync(ct); var scope = await service.GetSupplierScopeAsync(actor, ct); return Results.Created("/api/supplier/shipments", await service.CreateInboundShipmentAsync(request, scope, http.Request.Headers["Idempotency-Key"].ToString(), actor, ct)); });
supplier.MapPost("/invoices", async (InvoiceIngestionRequest request, ApplicationUserResolver user, SupplyChainService service, CancellationToken ct) => { var actor = await user.GetRequiredUserIdAsync(ct); var scope = await service.GetSupplierScopeAsync(actor, ct); return Results.Created("/api/supplier/invoices", await service.IngestInvoiceAsync(request, scope, actor, ct)); });

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
static string ApprovalReason(string? reason) => string.IsNullOrWhiteSpace(reason) ? "Approved through the operations console." : reason.Trim()[..Math.Min(reason.Trim().Length, 500)];
static async Task EvictPublicOutputAsync(IOutputCacheStore outputCache, CancellationToken cancellationToken)
{
    await outputCache.EvictByTagAsync("public-products", cancellationToken);
    await outputCache.EvictByTagAsync("public-storefront", cancellationToken);
}

public partial class Program;
