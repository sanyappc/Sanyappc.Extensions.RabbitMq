using System.Diagnostics;
using System.Text.Json;

using Microsoft.Extensions.Logging;

using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Sanyappc.Extensions.RabbitMq
{
    internal partial class RabbitMqPublisher(
        ILogger<RabbitMqPublisher> logger,
        IRabbitMqChannelFactory rabbitMqChannelFactory,
        string connectionName,
        string queueName) : IRabbitMqPublisher
    {
        private readonly ILogger<RabbitMqPublisher> logger = logger;
        private readonly IRabbitMqChannelFactory rabbitMqChannelFactory = rabbitMqChannelFactory;
        private readonly string connectionName = connectionName;
        private readonly string queueName = queueName;

        public async ValueTask PublishAsync(ReadOnlyMemory<byte> body, CancellationToken cancellationToken = default)
        {
            logger.LogInformation("Executing Rabbit Publisher with connection \"{}\"", connectionName);

            using IChannel channel = await rabbitMqChannelFactory.CreateChannelAsync(connectionName, cancellationToken)
                .ConfigureAwait(false);

            await channel.QueueDeclareAsync(queueName, true, false, false, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            BasicProperties properties = new();
            properties.Inject(Activity.Current);

            await channel.BasicPublishAsync(string.Empty, queueName, false, properties, body, cancellationToken)
                .ConfigureAwait(false);
        }

        public async ValueTask PublishAsync<T>(T body, JsonSerializerOptions? options = null, CancellationToken cancellationToken = default)
        {
            await PublishAsync(RabbitMqMessage.SerializeBody(body, options), cancellationToken)
              .ConfigureAwait(false);
        }

        public async ValueTask<TOut> PublishAsync<TIn, TOut>(TIn body, JsonSerializerOptions? options = null, CancellationToken cancellationToken = default)
        {
            const string replyTo = "amq.rabbitmq.reply-to";

            using IChannel channel = await rabbitMqChannelFactory.CreateChannelAsync(connectionName, cancellationToken)
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

            await channel.QueueDeclareAsync(queueName, true, false, false, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            BasicProperties properties = new();
            properties.Inject(Activity.Current);
            properties.ReplyTo = replyTo;

            await channel.BasicPublishAsync(string.Empty, queueName, false, properties, RabbitMqMessage.SerializeBody(body, options), cancellationToken)
                .ConfigureAwait(false);

            await semaphoreSlim.WaitAsync(cancellationToken)
                .ConfigureAwait(false);

            if (replyException is not null)
                throw replyException;

            return replyBody ?? throw new InvalidOperationException();
        }

        //public async ValueTask DisposeAsync()
        //{
        //    if (channel?.IsOpen == true)
        //    {
        //        await channel.CloseAsync();
        //    }
        //    channel?.Dispose();
        //}
    }
}
