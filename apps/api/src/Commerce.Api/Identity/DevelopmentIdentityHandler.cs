using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Commerce.Api.Identity;

public static class DevelopmentIdentityDefaults
{
    public const string Scheme = "DevelopmentIdentity";
}

public sealed class DevelopmentIdentityHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IConfiguration configuration,
    IWebHostEnvironment environment) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!environment.IsDevelopment() || !configuration.GetValue("ALLOW_DEVELOPMENT_IDENTITY", false))
            return Task.FromResult(AuthenticateResult.NoResult());

        var subject = Request.Headers["X-Customer-Id"].ToString().Trim();
        var isAdmin = string.Equals(Request.Headers["X-Admin"], "true", StringComparison.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(subject) && !isAdmin) return Task.FromResult(AuthenticateResult.NoResult());
        if (string.IsNullOrWhiteSpace(subject)) subject = "development-admin";
        if (subject.Length > 200) return Task.FromResult(AuthenticateResult.Fail("Development subject is too long."));

        var claims = new List<Claim>
        {
            new("sub", subject),
            new("iss", "development"),
            new(ClaimTypes.Name, subject),
            new(CommerceClaims.Permission, CommercePermissions.Customer)
        };
        if (isAdmin)
        {
            claims.AddRange([
                new(CommerceClaims.Permission, CommercePermissions.CatalogView),
                new(CommerceClaims.Permission, CommercePermissions.CatalogManage),
                new(CommerceClaims.Permission, CommercePermissions.InventoryView),
                new(CommerceClaims.Permission, CommercePermissions.InventoryManage),
                new(CommerceClaims.Permission, CommercePermissions.OperationsView),
                new(CommerceClaims.Permission, CommercePermissions.PricingView),
                new(CommerceClaims.Permission, CommercePermissions.PricingManage),
                new(CommerceClaims.Permission, CommercePermissions.PricingApprove),
                new(CommerceClaims.Permission, CommercePermissions.DemandView),
                new(CommerceClaims.Permission, CommercePermissions.DemandManage),
                new(CommerceClaims.Permission, CommercePermissions.ReplenishmentView),
                new(CommerceClaims.Permission, CommercePermissions.ReplenishmentManage),
                new(CommerceClaims.Permission, CommercePermissions.PromotionsView),
                new(CommerceClaims.Permission, CommercePermissions.PromotionsManage),
                new(CommerceClaims.Permission, CommercePermissions.OperationsAudit),
                new(CommerceClaims.Permission, CommercePermissions.SuppliersView),
                new(CommerceClaims.Permission, CommercePermissions.SuppliersManage),
                new(CommerceClaims.Permission, CommercePermissions.ProcurementView),
                new(CommerceClaims.Permission, CommercePermissions.ProcurementManage),
                new(CommerceClaims.Permission, CommercePermissions.ProcurementApprove),
                new(CommerceClaims.Permission, CommercePermissions.ReceivingView),
                new(CommerceClaims.Permission, CommercePermissions.ReceivingManage),
                new(CommerceClaims.Permission, CommercePermissions.InvoicesView),
                new(CommerceClaims.Permission, CommercePermissions.InvoicesManage),
                new(CommerceClaims.Permission, CommercePermissions.InvoicesMatch),
                new(CommerceClaims.Permission, CommercePermissions.WarehouseTasksView),
                new(CommerceClaims.Permission, CommercePermissions.WarehouseTasksManage),
                new(CommerceClaims.Permission, CommercePermissions.CycleCountsView),
                new(CommerceClaims.Permission, CommercePermissions.CycleCountsManage),
                new(CommerceClaims.Permission, CommercePermissions.CycleCountsReconcile),
                new(CommerceClaims.Permission, CommercePermissions.StockTransfersView),
                new(CommerceClaims.Permission, CommercePermissions.StockTransfersManage),
                new(CommerceClaims.Permission, CommercePermissions.PaymentsView),
                new(CommerceClaims.Permission, CommercePermissions.PaymentsRefund),
                new(CommerceClaims.Permission, CommercePermissions.PaymentsRefundLarge),
                new(CommerceClaims.Permission, CommercePermissions.PaymentsCapture),
                new(CommerceClaims.Permission, CommercePermissions.PaymentsReconcile),
                new(CommerceClaims.Permission, CommercePermissions.AdministrationAccess)
            ]);
        }
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
    }
}
