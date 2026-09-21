namespace Commerce.Application;

public sealed class GenericQrInvoiceResolver : IInvoiceResolver
{
    public bool Supports(string provider) => provider.Equals("GENERIC", StringComparison.OrdinalIgnoreCase) || provider.Equals("SUPPLIER", StringComparison.OrdinalIgnoreCase);

    public void ValidatePayload(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload) || payload.Length > 65536) throw CommerceErrors.Validation("Invoice payload is empty or too large.");
        if (Uri.TryCreate(payload.Trim(), UriKind.Absolute, out _)) throw CommerceErrors.Validation("Generic invoice ingestion does not accept URLs.");
    }
}

public sealed class FbrInvoiceResolver : IInvoiceResolver
{
    public bool Supports(string provider) => provider.Equals("FBR", StringComparison.OrdinalIgnoreCase);
    public void ValidatePayload(string payload) => ValidateAllowedHttpsUri(payload, "fbr.gov.pk");

    internal static void ValidateAllowedHttpsUri(string payload, string allowedDomain)
    {
        if (!Uri.TryCreate(payload.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || !(uri.Host.Equals(allowedDomain, StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith($".{allowedDomain}", StringComparison.OrdinalIgnoreCase)))
            throw CommerceErrors.Validation("The fiscal QR URL is not on the configured provider domain.");
        if (!string.IsNullOrEmpty(uri.UserInfo) || !uri.IsDefaultPort) throw CommerceErrors.Validation("The fiscal QR URL contains unsupported authority components.");
        // The URI is retained as evidence only. This service never dereferences scanned URLs.
    }
}

public sealed class SrbInvoiceResolver : IInvoiceResolver
{
    public bool Supports(string provider) => provider.Equals("SRB", StringComparison.OrdinalIgnoreCase);
    public void ValidatePayload(string payload) => FbrInvoiceResolver.ValidateAllowedHttpsUri(payload, "srb.gos.pk");
}
