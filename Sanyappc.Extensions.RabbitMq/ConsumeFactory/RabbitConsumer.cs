using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Sanyappc.Extensions.RabbitMq.ConsumeFactory
{
    public class RabbitConsumer(
        ILogger<RabbitConsumer> logger,
        IRabbitMqChannelFactory rabbitMqChannelFactory,
        IServiceScopeFactory serviceScopeFactory,
        string connectionName,
        string queue) : IRabbitConsumer
    {
        private readonly ILogger<RabbitConsumer> logger = logger;
        private readonly IRabbitMqChannelFactory rabbitMqChannelFactory = rabbitMqChannelFactory;
        protected readonly IServiceScopeFactory serviceScopeFactory = serviceScopeFactory;
        protected readonly string queue = queue;
        protected readonly string connectionName = connectionName;

        public async Task ExecuteAsync<TService>(CancellationToken stoppingToken = default) where TService : IRabbitMqMessageProcessingService
        {
            logger.LogInformation("Rabbit Consumer executing with connectionName={}, queueName={}", connectionName, queue);

            using IChannel channel = await rabbitMqChannelFactory.CreateChannelAsync(connectionName, stoppingToken)
                .ConfigureAwait(false);

            await channel.QueueDeclareAsync(queue, true, false, false, cancellationToken: stoppingToken)
                .ConfigureAwait(false);

            AsyncEventingBasicConsumer consumer = new(channel);
            consumer.ReceivedAsync += async (object sender, BasicDeliverEventArgs @event) =>
            {
                using Activity activity = @event.BasicProperties.StartActivity();

                AsyncServiceScope serviceScope = serviceScopeFactory.CreateAsyncScope();
                await using (serviceScope.ConfigureAwait(false))
                {
                    TService scopedMessageProcessingService = serviceScope.ServiceProvider.GetRequiredService<TService>();

                    await scopedMessageProcessingService.ProcessMessageAsync(new RabbitMqMessage(channel, @event), stoppingToken)
                        .ConfigureAwait(false);
                }
            };

            await channel.BasicConsumeAsync(queue, false, consumer, stoppingToken)
                .ConfigureAwait(false);

            await Task.Delay(Timeout.Infinite, stoppingToken)
                .ConfigureAwait(false);
        }
    }
}
