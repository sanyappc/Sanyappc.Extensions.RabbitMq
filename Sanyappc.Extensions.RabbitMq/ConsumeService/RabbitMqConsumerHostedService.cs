using Microsoft.Extensions.Hosting;

namespace Sanyappc.Extensions.RabbitMq
{
    internal class RabbitMqConsumerHostedService<T>(IRabbitMqConsumeService consumeService, string queue) : BackgroundService
        where T : IRabbitMqMessageProcessingService
    {
        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            return consumeService.ConsumeAsync<T>(queue, stoppingToken);
        }
    }
}
