using System.Diagnostics;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Sanyappc.Extensions.RabbitMq.ConsumeFactory
{
    internal partial class RabbitMqConsumer(
        ILogger<RabbitMqConsumer> logger,
        IServiceProvider serviceProvider,
        IRabbitMqChannelFactory rabbitMqChannelFactory,
        string connectionName,
        string queueName) : IRabbitMqConsumer
    {
        private readonly ILogger<RabbitMqConsumer> logger = logger;
        private readonly IServiceProvider serviceProvider = serviceProvider;
        private readonly IRabbitMqChannelFactory rabbitMqChannelFactory = rabbitMqChannelFactory;

        private readonly string connectionName = connectionName;
        private readonly string queueName = queueName;

        public async Task ConsumeAsync<TService>(CancellationToken stoppingToken = default) where TService : IRabbitMqMessageProcessingService
        {
            logger.LogInformation("Executing Rabbit Consumer with coonection \"{}\"", connectionName);

            using IChannel channel = await rabbitMqChannelFactory.CreateChannelAsync(connectionName, stoppingToken)
                .ConfigureAwait(false);

            await channel.QueueDeclareAsync(queueName, true, false, false, cancellationToken: stoppingToken)
                .ConfigureAwait(false);

            AsyncEventingBasicConsumer consumer = new(channel);
            consumer.ReceivedAsync += async (sender, @event) =>
            {
                await ProcessMessageAsync<TService>(@event, channel, stoppingToken);
            };

            await channel.BasicConsumeAsync(queueName, false, consumer, stoppingToken)
                .ConfigureAwait(false);

            await Task.Delay(Timeout.Infinite, stoppingToken)
                .ConfigureAwait(false);
        }

        private async Task ProcessMessageAsync<TService>(BasicDeliverEventArgs @event, IChannel channel, CancellationToken cancellationToken)
        where TService : IRabbitMqMessageProcessingService
        {
            using Activity activity = @event.BasicProperties.StartActivity();

            var serviceScopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();
            using AsyncServiceScope serviceScope = serviceScopeFactory.CreateAsyncScope();

            await using (serviceScope.ConfigureAwait(false))
            {
                TService scopedMessageProcessingService = serviceScope.ServiceProvider.GetRequiredService<TService>();

                await scopedMessageProcessingService.ProcessMessageAsync(new RabbitMqMessage(channel, @event), cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }
}
