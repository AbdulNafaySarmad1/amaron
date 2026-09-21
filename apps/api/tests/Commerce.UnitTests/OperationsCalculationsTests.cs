using Commerce.Application;
using Xunit;

namespace Commerce.UnitTests;

public sealed class OperationsCalculationsTests
{
    [Fact]
    public void Margin_and_markdown_calculations_preserve_business_units()
    {
        Assert.Equal(25m, OperationsCalculations.GrossMarginPercent(80m, 60m));
        Assert.Equal(20m, OperationsCalculations.MarkdownAmount(100m, 80m));
        Assert.Equal(20m, OperationsCalculations.MarkdownPercent(100m, 80m));
    }

    [Fact]
    public void Inventory_calculations_account_for_reservations_and_safety_stock()
    {
        Assert.Equal(65, OperationsCalculations.AvailableToSell(100, 20, 10, 5));
        Assert.Equal(13m, OperationsCalculations.DaysOfSupply(65, 5));
        Assert.Equal(45m, OperationsCalculations.ReorderPoint(5, 7, 10));
    }

    [Fact]
    public void Forecast_metrics_report_error_magnitude_and_direction()
    {
        var metrics = OperationsCalculations.ForecastMetrics([10m, 20m, 30m], [12m, 18m, 33m]);

        Assert.Equal(2.3333m, metrics.Mae);
        Assert.Equal(2.3805m, metrics.Rmse);
        Assert.Equal(11.6667m, metrics.Wape);
        Assert.Equal(1m, metrics.Bias);
    }

    [Fact]
    public void Undefined_ratios_return_null_instead_of_inventing_values()
    {
        Assert.Null(OperationsCalculations.GrossMarginPercent(0, 1));
        Assert.Null(OperationsCalculations.DaysOfSupply(10, 0));
        Assert.Null(OperationsCalculations.EconomicOrderQuantity(100, 10, 0));
        Assert.Null(OperationsCalculations.ArcElasticity(10, 10, 5, 6));
    }
}
