using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;

namespace Commerce.Api.Identity;

public static class CommerceClaims
{
    public const string Permission = "commerce:permission";
    public const string Group = "commerce:group";
}

public static class CommercePermissions
{
    public const string Customer = "customer";
    public const string CatalogView = "catalog-view";
    public const string CatalogManage = "catalog-manage";
    public const string InventoryView = "inventory-view";
    public const string InventoryManage = "inventory-manage";
    public const string OrdersViewAny = "orders-view-any";
    public const string OrdersManage = "orders-manage";
    public const string ReviewsCreate = "reviews-create";
    public const string ReviewsModerate = "reviews-moderate";
    public const string SecurityAuditor = "security-auditor";
    public const string SecurityAdmin = "security-admin";
    public const string AdministrationAccess = "administration-access";
    public const string OperationsView = "operations-view";
    public const string PricingView = "pricing-view";
    public const string PricingManage = "pricing-manage";
    public const string PricingApprove = "pricing-approve";
    public const string DemandView = "demand-view";
    public const string DemandManage = "demand-manage";
    public const string ReplenishmentView = "replenishment-view";
    public const string ReplenishmentManage = "replenishment-manage";
    public const string PromotionsView = "promotions-view";
    public const string PromotionsManage = "promotions-manage";
    public const string OperationsAudit = "operations-audit";
}

public static class CommercePolicies
{
    public const string CustomerAccess = "Customer.Access";
    public const string CatalogRead = "Catalog.Read";
    public const string CatalogManage = "Catalog.Manage";
    public const string InventoryRead = "Inventory.Read";
    public const string InventoryManage = "Inventory.Manage";
    public const string OrdersReadOwn = "Orders.ReadOwn";
    public const string OrdersReadAny = "Orders.ReadAny";
    public const string OrdersManage = "Orders.Manage";
    public const string ReviewsCreate = "Reviews.Create";
    public const string ReviewsModerate = "Reviews.Moderate";
    public const string SecurityAudit = "Security.Audit";
    public const string AdministrationAccess = "Administration.Access";
    public const string OperationsRead = "Operations.Read";
    public const string PricingRead = "Pricing.Read";
    public const string PricingManage = "Pricing.Manage";
    public const string PricingApprove = "Pricing.Approve";
    public const string DemandRead = "Demand.Read";
    public const string DemandManage = "Demand.Manage";
    public const string ReplenishmentRead = "Replenishment.Read";
    public const string ReplenishmentManage = "Replenishment.Manage";
    public const string PromotionsRead = "Promotions.Read";
    public const string PromotionsManage = "Promotions.Manage";
    public const string OperationsAudit = "Operations.Audit";

    public static void AddCommercePolicies(this AuthorizationOptions options)
    {
        static void Subject(AuthorizationPolicyBuilder policy) => policy.RequireAuthenticatedUser().RequireClaim("sub");
        static void Permission(AuthorizationPolicyBuilder policy, string permission) => policy.RequireAuthenticatedUser().RequireClaim(CommerceClaims.Permission, permission);

        options.AddPolicy(CustomerAccess, policy => Subject(policy));
        options.AddPolicy(CatalogRead, policy => Permission(policy, CommercePermissions.CatalogView));
        options.AddPolicy(CatalogManage, policy => Permission(policy, CommercePermissions.CatalogManage));
        options.AddPolicy(InventoryRead, policy => Permission(policy, CommercePermissions.InventoryView));
        options.AddPolicy(InventoryManage, policy => Permission(policy, CommercePermissions.InventoryManage));
        options.AddPolicy(OrdersReadOwn, policy => Subject(policy));
        options.AddPolicy(OrdersReadAny, policy => Permission(policy, CommercePermissions.OrdersViewAny));
        options.AddPolicy(OrdersManage, policy => Permission(policy, CommercePermissions.OrdersManage));
        options.AddPolicy(ReviewsCreate, policy => Permission(policy, CommercePermissions.ReviewsCreate));
        options.AddPolicy(ReviewsModerate, policy => Permission(policy, CommercePermissions.ReviewsModerate));
        options.AddPolicy(SecurityAudit, policy => Permission(policy, CommercePermissions.SecurityAuditor));
        options.AddPolicy(AdministrationAccess, policy => Permission(policy, CommercePermissions.AdministrationAccess));
        options.AddPolicy(OperationsRead, policy => Permission(policy, CommercePermissions.OperationsView));
        options.AddPolicy(PricingRead, policy => Permission(policy, CommercePermissions.PricingView));
        options.AddPolicy(PricingManage, policy => Permission(policy, CommercePermissions.PricingManage));
        options.AddPolicy(PricingApprove, policy => Permission(policy, CommercePermissions.PricingApprove));
        options.AddPolicy(DemandRead, policy => Permission(policy, CommercePermissions.DemandView));
        options.AddPolicy(DemandManage, policy => Permission(policy, CommercePermissions.DemandManage));
        options.AddPolicy(ReplenishmentRead, policy => Permission(policy, CommercePermissions.ReplenishmentView));
        options.AddPolicy(ReplenishmentManage, policy => Permission(policy, CommercePermissions.ReplenishmentManage));
        options.AddPolicy(PromotionsRead, policy => Permission(policy, CommercePermissions.PromotionsView));
        options.AddPolicy(PromotionsManage, policy => Permission(policy, CommercePermissions.PromotionsManage));
        options.AddPolicy(OperationsAudit, policy => Permission(policy, CommercePermissions.OperationsAudit));
    }
}

public sealed class KeycloakClaimsTransformation(IConfiguration configuration) : IClaimsTransformation
{
    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity is not ClaimsIdentity identity || !identity.IsAuthenticated || identity.HasClaim(claim => claim.Type == CommerceClaims.Permission))
            return Task.FromResult(principal);

        var permissions = new HashSet<string>(StringComparer.Ordinal);
        foreach (var claim in identity.FindAll("role")) AddValue(claim.Value, permissions);
        AddNestedRoles(identity.FindFirst("resource_access")?.Value, configuration["AUTH_ROLE_CLIENT_ID"] ?? "commerce-api", permissions);

        foreach (var permission in permissions)
            identity.AddClaim(new Claim(CommerceClaims.Permission, permission));

        foreach (var groupClaim in identity.FindAll("groups").ToArray())
            AddValue(groupClaim.Value, value => identity.AddClaim(new Claim(CommerceClaims.Group, value)));

        if (!identity.HasClaim(claim => claim.Type == ClaimTypes.Name))
        {
            var name = identity.FindFirst("name")?.Value ?? identity.FindFirst("preferred_username")?.Value;
            if (!string.IsNullOrWhiteSpace(name)) identity.AddClaim(new Claim(ClaimTypes.Name, name));
        }
        return Task.FromResult(principal);
    }

    private static void AddNestedRoles(string? value, string clientId, HashSet<string> permissions)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        try
        {
            using var document = JsonDocument.Parse(value);
            if (!document.RootElement.TryGetProperty(clientId, out var client) || !client.TryGetProperty("roles", out var roles) || roles.ValueKind != JsonValueKind.Array) return;
            foreach (var role in roles.EnumerateArray())
                if (role.ValueKind == JsonValueKind.String && role.GetString() is { Length: > 0 } permission) permissions.Add(permission);
        }
        catch (JsonException)
        {
            // Malformed role claims never grant permissions.
        }
    }

    private static void AddValue(string value, HashSet<string> values) => AddValue(value, parsed => values.Add(parsed));

    private static void AddValue(string value, Action<string> add)
    {
        if (value.StartsWith('['))
        {
            try
            {
                using var document = JsonDocument.Parse(value);
                foreach (var item in document.RootElement.EnumerateArray())
                    if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } parsed) add(parsed);
                return;
            }
            catch (JsonException) { return; }
        }
        if (!string.IsNullOrWhiteSpace(value)) add(value);
    }
}
