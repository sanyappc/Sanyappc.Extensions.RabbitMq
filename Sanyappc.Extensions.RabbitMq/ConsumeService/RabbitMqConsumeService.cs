using System.Diagnostics;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Sanyappc.Extensions.RabbitMq
{
    internal class RabbitMqConsumeService(
        ILogger<RabbitMqConsumeService> logger,
        IRabbitMqChannelFactory rabbitMqChannelFactory,
        IServiceScopeFactory serviceScopeFactory,
        IOptions<RabbitMqConsumerOptions> options) : IRabbitMqConsumeService
    {
        private readonly ILogger<RabbitMqConsumeService> logger = logger;
        private readonly IRabbitMqChannelFactory rabbitMqChannelFactory = rabbitMqChannelFactory;
        private readonly IServiceScopeFactory serviceScopeFactory = serviceScopeFactory;
        private readonly string connectionName = options.Value.ConnectionName;
        private readonly string queueName = options.Value.QueueName;


        public async ValueTask ConsumeAsync<T>(CancellationToken cancellationToken = default) where T : IRabbitMqMessageProcessingService
        {
            logger.LogInformation("connectionName={}", connectionName);

            using IChannel channel = await rabbitMqChannelFactory.CreateChannelAsync(connectionName, cancellationToken)
                .ConfigureAwait(false);

            await channel.QueueDeclareAsync(queueName, true, false, false, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            AsyncEventingBasicConsumer consumer = new(channel);
            consumer.ReceivedAsync += async (object sender, BasicDeliverEventArgs @event) =>
            {
                using Activity activity = @event.BasicProperties.StartActivity();
                using IDisposable? loggerScope = logger.BeginScope(
                    "MessageId: {messageId}", @event.BasicProperties.MessageId ?? $"{@event.DeliveryTag}");

                AsyncServiceScope serviceScope = serviceScopeFactory.CreateAsyncScope();
                await using (serviceScope.ConfigureAwait(false))
                {
                    T scopedMessageProcessingService = serviceScope.ServiceProvider.GetRequiredService<T>();

                    await scopedMessageProcessingService.ProcessMessageAsync(new RabbitMqMessage(channel, @event), cancellationToken)
                        .ConfigureAwait(false);
                }
            };

            await channel.BasicConsumeAsync(queueName, false, consumer, cancellationToken)
               .ConfigureAwait(false);

            await Task.Delay(TimeSpan.FromSeconds(20), cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
