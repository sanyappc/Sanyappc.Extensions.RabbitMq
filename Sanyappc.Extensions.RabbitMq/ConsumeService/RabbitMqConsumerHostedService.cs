using Microsoft.Extensions.Hosting;

namespace Sanyappc.Extensions.RabbitMq
{
    internal class RabbitMqConsumerHostedService<T>(IRabbitMqConsumeService consumeService, string queue) : BackgroundService
        where T : class, IRabbitMqMessageProcessingService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                await consumeService.ConsumeAsync<T>(queue, stoppingToken)
                    .ConfigureAwait(false);
            }
            catch (Exception) when (!stoppingToken.IsCancellationRequested)
            {
                Environment.ExitCode = 1;
                throw;
            }
        }
    }
}
