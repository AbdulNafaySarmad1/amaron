using Commerce.Application;
using Commerce.Domain;
using Xunit;

namespace Commerce.UnitTests;

public sealed class HighlightTests
{
    private static ProductAttribute A(string label, string value, bool highlight) => new() { Label = label, Value = value, Highlight = highlight };

    [Fact]
    public void Cards_show_only_flagged_attributes_in_catalog_order_at_most_three()
    {
        var highlights = CatalogService.Highlights([A("Author", "R. Haddad", true), A("ISBN", "979-8", false), A("Format", "Hardcover", true), A("Pages", "256", true), A("Language", "English", true)]);
        Assert.Equal(["Author", "Format", "Pages"], highlights.Select(x => x.Label));
    }

    [Fact]
    public void Empty_values_are_never_shown() =>
        Assert.Equal(["Capacity"], CatalogService.Highlights([A("Energy rating", " ", true), A("Capacity", "500 L", true)]).Select(x => x.Label));

    [Fact]
    public void Products_without_attributes_have_no_highlights() => Assert.Empty(CatalogService.Highlights([]));
}
