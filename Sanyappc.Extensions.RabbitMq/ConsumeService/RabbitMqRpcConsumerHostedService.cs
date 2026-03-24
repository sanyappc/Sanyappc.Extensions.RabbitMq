using Microsoft.Extensions.Hosting;

namespace Sanyappc.Extensions.RabbitMq;

internal class RabbitMqRpcConsumerHostedService<T>(IRabbitMqConsumeService consumeService, string queue) : BackgroundService
    where T : class, IRabbitMqRpcMessageProcessingService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await consumeService.ConsumeRpcAsync<T>(queue, stoppingToken)
                .ConfigureAwait(false);
        }
        catch (Exception) when (!stoppingToken.IsCancellationRequested)
        {
            Environment.ExitCode = 1;
            throw;
        }
    }
}
