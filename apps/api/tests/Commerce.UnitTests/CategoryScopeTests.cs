using Commerce.Application;
using Commerce.Contracts;
using Xunit;

namespace Commerce.UnitTests;

public sealed class CategoryScopeTests
{
    private static readonly Guid Electronics = Guid.NewGuid(), Audio = Guid.NewGuid(), Headphones = Guid.NewGuid(), Books = Guid.NewGuid(), Appliances = Guid.NewGuid(), Refrigerators = Guid.NewGuid();

    private static readonly CategoryDto[] Tree =
    [
        new(Electronics, "electronics", "Electronics", null),
        new(Audio, "audio", "Audio", Electronics),
        new(Headphones, "headphones", "Headphones", Audio),
        new(Books, "books", "Books", null),
        new(Appliances, "appliances", "Appliances", null),
        new(Refrigerators, "refrigerators", "Refrigerators", Appliances),
    ];

    [Fact]
    public void Parent_scope_includes_every_descendant_and_nothing_else() =>
        Assert.Equal(new HashSet<Guid> { Electronics, Audio, Headphones }, CatalogService.CategoryScope(Tree, "electronics").ToHashSet());

    [Theory]
    [InlineData("books")]
    [InlineData("refrigerators")]
    [InlineData("headphones")]
    public void Leaf_scope_is_only_itself(string slug) =>
        Assert.Equal([Tree.Single(x => x.Slug == slug).Id], CatalogService.CategoryScope(Tree, slug));

    [Fact]
    public void Sibling_subtrees_never_leak()
    {
        var appliances = CatalogService.CategoryScope(Tree, "appliances");
        Assert.Equal(2, appliances.Count);
        Assert.DoesNotContain(Books, appliances);
        Assert.DoesNotContain(Electronics, appliances);
    }

    [Fact]
    public void Unknown_slug_is_empty_not_everything() => Assert.Empty(CatalogService.CategoryScope(Tree, "no-such-category"));

    [Fact]
    public void Slug_match_is_case_insensitive() => Assert.Equal(3, CatalogService.CategoryScope(Tree, "Electronics").Count);

    [Fact]
    public void Cyclic_parent_data_terminates()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid();
        Assert.Equal(2, CatalogService.CategoryScope([new(a, "a", "A", b), new(b, "b", "B", a)], "a").Count);
    }
}
