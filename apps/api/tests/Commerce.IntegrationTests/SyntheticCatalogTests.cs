using System.Text.RegularExpressions;
using Commerce.Infrastructure;
using Xunit;

namespace Commerce.IntegrationTests;

public sealed class SyntheticCatalogTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Full_catalog_is_25_departments_of_5000_realistic_unique_products()
    {
        var products = SyntheticCatalog.Generate(125_000, Now).ToList();
        Assert.Equal(125_000, products.Count);
        Assert.Equal(25, SyntheticCatalog.Departments.Length);
        var departmentOf = SyntheticCatalog.Departments.SelectMany(d => d.Subcategories.Select(s => (s.Slug, d.Slug))).ToDictionary(x => x.Item1, x => x.Item2);
        Assert.All(products.GroupBy(p => departmentOf[p.CategorySlug]), g => Assert.Equal(5_000, g.Count()));

        var slugs = SyntheticCatalog.Departments.Select(d => d.Slug).Concat(departmentOf.Keys).ToList();
        Assert.Equal(slugs.Count, slugs.Distinct().Count());
        Assert.Equal(products.Count, products.Select(p => p.Slug).Distinct().Count());
        Assert.Equal(products.Count, products.Select(p => p.Sku).Distinct().Count());
        Assert.All(products, p => Assert.Matches("^[a-z0-9]+(-[a-z0-9]+)*$", p.Slug));
        Assert.DoesNotContain(products, p => Regex.IsMatch(p.Title, @"^Product \d+"));
        // Titles repeat only where a real catalog would (same brand, model and headline spec), never en masse.
        Assert.True(products.Select(p => p.Title).Distinct().Count() > products.Count * 0.6);
        Assert.All(products, p => Assert.True(p.Price > 0 && (p.ListPrice is null || p.ListPrice > p.Price) && p.UnitCost < p.Price));
        Assert.All(products, p => Assert.All(p.Ratings, r => Assert.InRange(r, 1, 5)));
    }

    [Theory]
    [InlineData("laptops", "Screen size", "RAM", "Storage", "CPU", "GPU", "Display", "Battery", "Weight")]
    [InlineData("refrigerators", "Capacity", "Energy rating", "Door configuration", "Compressor", "Dimensions")]
    [InlineData("fiction", "Author", "Format", "Pages", "Language", "Publisher", "ISBN")]
    public void Each_product_type_carries_its_own_attributes(string category, params string[] labels)
    {
        var products = SyntheticCatalog.Generate(125_000, Now).Where(p => p.CategorySlug == category).ToList();
        Assert.NotEmpty(products);
        Assert.All(products, p => Assert.Equal(labels, p.Attributes.Select(a => a.Label)));
        Assert.All(products, p => Assert.Equal(3, p.Attributes.Count(a => a.Highlight)));
    }

    [Fact]
    public void Nouns_pin_only_real_attributes_and_prices_stay_within_their_noun()
    {
        foreach (var sub in SyntheticCatalog.Departments.SelectMany(d => d.Subcategories))
            foreach (var noun in sub.Nouns)
            {
                Assert.All(noun.Fixed?.Keys ?? [], label => Assert.Contains(label, sub.Attributes.Select(a => a.Label)));
                Assert.True((noun.MinPrice ?? sub.MinPrice) < (noun.MaxPrice ?? sub.MaxPrice), $"{sub.Slug}/{noun.Name}");
            }
        // Earbuds are always in-ear; a winter tyre is always a winter tyre.
        var products = SyntheticCatalog.Generate(125_000, Now).ToList();
        Assert.All(products.Where(p => p.Title.Contains("Earbuds")), p => Assert.Equal("In-ear", p.Attributes.Single(a => a.Label == "Type").Value));
        Assert.All(products.Where(p => p.Title.Contains("Winter Tyre")), p => Assert.Equal("Winter", p.Attributes.Single(a => a.Label == "Season").Value));
        Assert.All(products.Where(p => p.Title.Contains("Espresso Machine")), p => Assert.InRange(p.Price, 199m, 1299.99m));
    }

    [Fact]
    public void Book_isbns_are_valid_and_unique()
    {
        var isbns = SyntheticCatalog.Generate(125_000, Now).Where(p => p.Kind == "book").Select(p => p.Attributes.Single(a => a.Label == "ISBN").Value).ToList();
        Assert.Equal(isbns.Count, isbns.Distinct().Count());
        Assert.All(isbns, isbn =>
        {
            var digits = isbn.Replace("-", "");
            Assert.Matches("^979[0-9]{10}$", digits);
            Assert.Equal(0, digits.Select((c, i) => (c - '0') * (i % 2 == 0 ? 1 : 3)).Sum() % 10);
        });
    }

    [Fact]
    public void Generation_is_deterministic()
    {
        var first = SyntheticCatalog.Generate(2_000, Now).Select(p => (p.Slug, p.Price, p.OnHand, string.Join(";", p.Attributes.Select(a => a.Value)))).ToList();
        var second = SyntheticCatalog.Generate(2_000, Now).Select(p => (p.Slug, p.Price, p.OnHand, string.Join(";", p.Attributes.Select(a => a.Value)))).ToList();
        Assert.Equal(first, second);
    }
}
