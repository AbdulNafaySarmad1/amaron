using Commerce.Application;

namespace Commerce.Api;

public sealed class OperationsPriceActivationService(IServiceScopeFactory scopeFactory, ILogger<OperationsPriceActivationService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<OperationsService>().ApplyDuePricesAsync("system:price-activator", stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception exception)
            {
                logger.LogError(exception, "Scheduled price activation failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
