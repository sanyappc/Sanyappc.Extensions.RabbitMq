using System.Diagnostics;
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

            BasicProperties properties = new();
            properties.Inject(Activity.Current);

            await channel.BasicPublishAsync(string.Empty, queue, false, properties, body, cancellationToken)
                .ConfigureAwait(false);
        }

        public async ValueTask PublishAsync<T>(string queue, T body, JsonSerializerOptions? options = null, CancellationToken cancellationToken = default)
        {
            await PublishAsync(queue, RabbitMqMessage.SerializeBody(body, options), cancellationToken)
              .ConfigureAwait(false);
        }

        public async ValueTask<TOut> PublishAsync<TIn, TOut>(string queue, TIn body, JsonSerializerOptions? options = null, CancellationToken cancellationToken = default)
        {
            const string replyTo = "amq.rabbitmq.reply-to";

            using IChannel channel = await rabbitMqChannelFactory.CreateChannelAsync(cancellationToken)
                .ConfigureAwait(false);

            using SemaphoreSlim semaphoreSlim = new(0, 1);

            TOut? replyBody = default;
            Exception? replyException = default;

            AsyncEventingBasicConsumer consumer = new(channel);
            consumer.ReceivedAsync += async (object sender, BasicDeliverEventArgs @event) =>
            {
                if (semaphoreSlim.CurrentCount != 0)
                    return;

                await Task.CompletedTask
                    .ConfigureAwait(false);

                try
                {
                    replyBody = RabbitMqMessage.DeserializeBody<TOut>(@event.Body.Span, options);
                }
                catch (Exception ex)
                {
                    replyException = ex;
                }

                semaphoreSlim.Release();
            };

            await channel.BasicConsumeAsync(replyTo, true, consumer, cancellationToken)
                .ConfigureAwait(false);

            await channel.QueueDeclareAsync(queue, false, false, false, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            BasicProperties properties = new();
            properties.Inject(Activity.Current);
            properties.ReplyTo = replyTo;

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
