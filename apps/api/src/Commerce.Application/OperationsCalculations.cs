namespace Commerce.Application;

public static class OperationsCalculations
{
    public static decimal Revenue(decimal price, decimal predictedQuantity) => decimal.Round(price * predictedQuantity, 4);
    public static decimal? GrossProfit(decimal price, decimal? unitCost, decimal predictedQuantity) => unitCost is null ? null : decimal.Round((price - unitCost.Value) * predictedQuantity, 4);
    public static decimal? GrossMarginPercent(decimal price, decimal? unitCost) => price <= 0 || unitCost is null ? null : decimal.Round((price - unitCost.Value) / price * 100m, 4);
    public static decimal? ContributionMarginPerUnit(decimal price, decimal? variableCost) => variableCost is null ? null : decimal.Round(price - variableCost.Value, 4);
    public static decimal? ContributionMarginRatio(decimal price, decimal? variableCost) => price <= 0 || variableCost is null ? null : decimal.Round((price - variableCost.Value) / price, 6);
    public static decimal MarkdownAmount(decimal originalPrice, decimal newPrice) => decimal.Round(originalPrice - newPrice, 4);
    public static decimal? MarkdownPercent(decimal originalPrice, decimal newPrice) => originalPrice <= 0 ? null : decimal.Round((originalPrice - newPrice) / originalPrice * 100m, 4);

    public static decimal? ArcElasticity(decimal firstPrice, decimal secondPrice, decimal firstQuantity, decimal secondQuantity)
    {
        var averagePrice = (firstPrice + secondPrice) / 2m;
        var averageQuantity = (firstQuantity + secondQuantity) / 2m;
        if (averagePrice == 0 || averageQuantity == 0 || firstPrice == secondPrice) return null;
        return decimal.Round(((secondQuantity - firstQuantity) / averageQuantity) / ((secondPrice - firstPrice) / averagePrice), 6);
    }

    public static int AvailableToSell(int onHand, int reserved, int safetyStock, int unavailable) => Math.Max(0, onHand - reserved - safetyStock - unavailable);
    public static decimal? DaysOfSupply(decimal availableInventory, decimal averageDailyDemand) => averageDailyDemand <= 0 ? null : decimal.Round(availableInventory / averageDailyDemand, 2);
    public static decimal ReorderPoint(decimal averageDailyDemand, int leadTimeDays, decimal safetyStock) => decimal.Round(Math.Max(0, averageDailyDemand) * Math.Max(0, leadTimeDays) + Math.Max(0, safetyStock), 2);
    public static decimal SafetyStock(decimal serviceFactor, decimal demandStandardDeviationDuringLeadTime) => decimal.Round(Math.Max(0, serviceFactor) * Math.Max(0, demandStandardDeviationDuringLeadTime), 2);
    public static decimal? EconomicOrderQuantity(decimal annualDemand, decimal orderingCost, decimal annualHoldingCostPerUnit) => annualDemand <= 0 || orderingCost <= 0 || annualHoldingCostPerUnit <= 0 ? null : decimal.Round((decimal)Math.Sqrt((double)(2m * annualDemand * orderingCost / annualHoldingCostPerUnit)), 2);
    public static decimal? ConversionRate(int orders, int qualifiedVisits) => qualifiedVisits <= 0 ? null : decimal.Round((decimal)orders / qualifiedVisits * 100m, 4);
    public static decimal? SellThroughRate(int unitsSold, int endingInventory) => unitsSold + endingInventory <= 0 ? null : decimal.Round((decimal)unitsSold / (unitsSold + endingInventory) * 100m, 4);

    public static (decimal Mae, decimal Rmse, decimal? Wape, decimal Bias) ForecastMetrics(IReadOnlyList<decimal> actual, IReadOnlyList<decimal> forecast)
    {
        if (actual.Count == 0 || actual.Count != forecast.Count) throw CommerceErrors.Validation("Actual and forecast series must have the same nonzero length.");
        var errors = actual.Zip(forecast, (a, f) => f - a).ToArray();
        var mae = errors.Average(x => Math.Abs(x));
        var rmse = (decimal)Math.Sqrt((double)errors.Average(x => x * x));
        var totalActual = actual.Sum(x => Math.Abs(x));
        decimal? wape = totalActual == 0 ? null : decimal.Round(errors.Sum(x => Math.Abs(x)) / totalActual * 100m, 4);
        return (decimal.Round(mae, 4), decimal.Round(rmse, 4), wape, decimal.Round(errors.Average(), 4));
    }
}
