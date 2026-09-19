using Commerce.Contracts;
using Xunit;

namespace Commerce.UnitTests;

public sealed class ContractTests
{
    [Fact]
    public void Cart_mutation_exposes_canonical_state_for_optimistic_ui()
    {
        var response = new CartMutationDto(Guid.NewGuid(), 2, new MoneyDto(39.98m, "USD"), null, "4");
        Assert.Equal(2, response.TotalQuantity);
        Assert.Equal(39.98m, response.Subtotal.Amount);
        Assert.Equal("4", response.Version);
    }
}
