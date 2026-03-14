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

        [LoggerMessage(Level = LogLevel.Debug, Message = "Publishing message to queue {Queue}")]
        private static partial void LogPublish(ILogger logger, string queue);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Sending request to queue {Queue}, awaiting reply")]
        private static partial void LogRequest(ILogger logger, string queue);

        public async Task PublishAsync(string queue, ReadOnlyMemory<byte> body, CancellationToken cancellationToken = default)
        {
            LogPublish(logger, queue);

            using Activity? activity = RabbitMqBasicPropertiesExtensions.StartPublishActivity(queue);

            using IChannel channel = await rabbitMqChannelFactory.CreateChannelAsync(cancellationToken)
                .ConfigureAwait(false);

            await channel.QueueDeclareAsync(queue, true, false, false, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            BasicProperties properties = new();
            properties.Inject(Activity.Current);

            await channel.BasicPublishAsync(string.Empty, queue, false, properties, body, cancellationToken)
                .ConfigureAwait(false);

            RabbitMqTelemetry.PublishedMessages.Add(1,
                new KeyValuePair<string, object?>("messaging.system", "rabbitmq"),
                new KeyValuePair<string, object?>("messaging.destination.name", queue));
        }

        public async Task PublishAsync<T>(string queue, T body, JsonSerializerOptions? options = null, CancellationToken cancellationToken = default)
        {
            await PublishAsync(queue, RabbitMqMessage.SerializeBody(body, options), cancellationToken)
              .ConfigureAwait(false);
        }

        public async Task<TOut> RequestAsync<TIn, TOut>(string queue, TIn body, JsonSerializerOptions? options = null, CancellationToken cancellationToken = default)
            where TOut : notnull
        {
            const string replyTo = "amq.rabbitmq.reply-to";

            LogRequest(logger, queue);

            using Activity? activity = RabbitMqBasicPropertiesExtensions.StartRequestActivity(queue);

            using IChannel channel = await rabbitMqChannelFactory.CreateChannelAsync(cancellationToken)
                .ConfigureAwait(false);

            TaskCompletionSource<TOut> replyTaskCompletionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);

            AsyncEventingBasicConsumer consumer = new(channel);
            consumer.ReceivedAsync += (object sender, BasicDeliverEventArgs @event) =>
            {
                try
                {
                    replyTaskCompletionSource.TrySetResult(RabbitMqMessage.DeserializeBody<TOut>(@event.Body.Span, options));
                }
                catch (Exception ex)
                {
                    replyTaskCompletionSource.TrySetException(ex);
                }

                return Task.CompletedTask;
            };

            await channel.BasicConsumeAsync(replyTo, true, consumer, cancellationToken)
                .ConfigureAwait(false);

            await channel.QueueDeclareAsync(queue, true, false, false, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            BasicProperties properties = new();
            properties.Inject(Activity.Current);
            properties.ReplyTo = replyTo;

            await channel.BasicPublishAsync(string.Empty, queue, false, properties, RabbitMqMessage.SerializeBody(body, options), cancellationToken)
                .ConfigureAwait(false);

            RabbitMqTelemetry.PublishedMessages.Add(1,
                new KeyValuePair<string, object?>("messaging.system", "rabbitmq"),
                new KeyValuePair<string, object?>("messaging.destination.name", queue));

            return await replyTaskCompletionSource.Task.WaitAsync(cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
