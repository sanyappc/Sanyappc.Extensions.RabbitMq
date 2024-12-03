using System.Text.Json;

using Microsoft.Extensions.Logging;

using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Sanyappc.Extensions.RabbitMq
{
    internal partial class RabbitMqPublishService(ILogger<RabbitMqPublishService> logger, IRabbitMqChannelFactory rabbitMqChannelFactory) : IRabbitMqPublishService
    {
        private readonly ILogger<RabbitMqPublishService> logger = logger;
        private readonly IRabbitMqChannelFactory rabbitMqChannelFactory = rabbitMqChannelFactory;

        public async ValueTask PublishAsync(string queue, ReadOnlyMemory<byte> body, CancellationToken cancellationToken = default)
        {
            using IChannel channel = await rabbitMqChannelFactory.CreateChannelAsync(cancellationToken)
                .ConfigureAwait(false);

            await channel.QueueDeclareAsync(queue, false, false, false, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            await channel.BasicPublishAsync(string.Empty, queue, body, cancellationToken)
              .ConfigureAwait(false);
        }

        public async ValueTask PublishAsync<T>(string queue, T body, JsonSerializerOptions? options = null, CancellationToken cancellationToken = default)
        {
            await PublishAsync(queue, RabbitMqMessage.SerializeBody(body, options), cancellationToken)
              .ConfigureAwait(false);
        }

        public async ValueTask<TOut> PublishAsync<TIn, TOut>(string queue, TIn body, JsonSerializerOptions? options = null, CancellationToken cancellationToken = default)
        {
            using IChannel channel = await rabbitMqChannelFactory.CreateChannelAsync(cancellationToken)
                .ConfigureAwait(false);

            QueueDeclareOk replyQueue = await channel.QueueDeclareAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            string correlationID = $"{Guid.NewGuid()}";

            using SemaphoreSlim semaphoreSlim = new(0, 1);

            TOut? replyBody = default;
            Exception? replyException = default;

            AsyncEventingBasicConsumer consumer = new(channel);
            consumer.ReceivedAsync += async (object sender, BasicDeliverEventArgs @event) =>
            {
                if (semaphoreSlim.CurrentCount != 0)
                    return;

                try
                {
                    if (@event.BasicProperties.CorrelationId != correlationID)
                        throw new InvalidOperationException();

                    replyBody = RabbitMqMessage.DeserializeBody<TOut>(@event.Body.Span, options);

                    await channel.BasicAckAsync(@event.DeliveryTag, false, cancellationToken)
                       .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    replyException = ex;

                    await channel.BasicRejectAsync(@event.DeliveryTag, false, cancellationToken)
                        .ConfigureAwait(false);
                }

                semaphoreSlim.Release();
            };

            await channel.BasicConsumeAsync(replyQueue.QueueName, false, consumer, cancellationToken)
                .ConfigureAwait(false);

            await channel.QueueDeclareAsync(queue, false, false, false, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            BasicProperties properties = new()
            {
                ReplyTo = replyQueue.QueueName,
                CorrelationId = correlationID
            };

            await channel.BasicPublishAsync(string.Empty, queue, false, properties, RabbitMqMessage.SerializeBody(body, options), cancellationToken)
                .ConfigureAwait(false);

            await semaphoreSlim.WaitAsync(cancellationToken)
                .ConfigureAwait(false);

            if (replyException is not null)
                throw replyException;

            return replyBody ?? throw new InvalidOperationException();
        }
    }
}
