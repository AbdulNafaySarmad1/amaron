using Commerce.Application;
using Xunit;

namespace Commerce.UnitTests;

public sealed class InvoiceResolverTests
{
    [Fact]
    public void Generic_resolver_rejects_urls()
    {
        var resolver = new GenericQrInvoiceResolver();
        Assert.Throws<CommerceException>(() => resolver.ValidatePayload("https://attacker.invalid/invoice"));
        resolver.ValidatePayload("invoice|INV-100|100");
    }

    [Theory]
    [InlineData("https://fbr.gov.pk/invoice/100")]
    [InlineData("https://verify.fbr.gov.pk/invoice/100")]
    public void Fbr_resolver_accepts_only_provider_https_hosts(string payload)
    {
        new FbrInvoiceResolver().ValidatePayload(payload);
    }

    [Theory]
    [InlineData("http://fbr.gov.pk/invoice/100")]
    [InlineData("https://fbr.gov.pk.attacker.invalid/invoice/100")]
    [InlineData("https://user@fbr.gov.pk/invoice/100")]
    [InlineData("https://fbr.gov.pk:8443/invoice/100")]
    public void Fbr_resolver_rejects_unsafe_authorities(string payload)
    {
        Assert.Throws<CommerceException>(() => new FbrInvoiceResolver().ValidatePayload(payload));
    }
}
