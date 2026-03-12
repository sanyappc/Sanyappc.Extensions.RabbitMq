using System.Diagnostics;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Sanyappc.Extensions.RabbitMq
{
    internal class RabbitMqConsumeService(ILogger<RabbitMqConsumeService> logger, IRabbitMqChannelFactory rabbitMqChannelFactory, IServiceScopeFactory serviceScopeFactory) : IRabbitMqConsumeService
    {
        private readonly ILogger<RabbitMqConsumeService> logger = logger;
        private readonly IRabbitMqChannelFactory rabbitMqChannelFactory = rabbitMqChannelFactory;
        private readonly IServiceScopeFactory serviceScopeFactory = serviceScopeFactory;

        public async Task ConsumeAsync<T>(string queue, CancellationToken cancellationToken = default)
            where T : IRabbitMqMessageProcessingService
        {
            using IChannel channel = await rabbitMqChannelFactory.CreateChannelAsync(cancellationToken)
                .ConfigureAwait(false);

            await channel.QueueDeclareAsync(queue, true, false, false, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            AsyncEventingBasicConsumer consumer = new(channel);
            consumer.ReceivedAsync += async (object sender, BasicDeliverEventArgs @event) =>
            {
                using Activity? activity = @event.BasicProperties.StartReceiveActivity(queue);
                using IDisposable? loggerScope = logger.BeginScope(new Dictionary<string, object?>
                {
                    ["Queue"] = queue,
                    ["MessageId"] = @event.BasicProperties.MessageId,
                    ["DeliveryTag"] = @event.DeliveryTag,
                    ["TraceId"] = activity?.TraceId.ToString(),
                    ["SpanId"] = activity?.SpanId.ToString(),
                });

                RabbitMqTelemetry.ReceivedMessages.Add(1,
                    new KeyValuePair<string, object?>("messaging.system", "rabbitmq"),
                    new KeyValuePair<string, object?>("messaging.destination.name", queue));

                long startTimestamp = Stopwatch.GetTimestamp();

                AsyncServiceScope serviceScope = serviceScopeFactory.CreateAsyncScope();
                await using (serviceScope.ConfigureAwait(false))
                {
                    T scopedMessageProcessingService = serviceScope.ServiceProvider.GetRequiredService<T>();

                    await scopedMessageProcessingService.ProcessMessageAsync(new RabbitMqMessage(channel, @event), cancellationToken)
                        .ConfigureAwait(false);
                }

                RabbitMqTelemetry.ProcessDuration.Record(
                    Stopwatch.GetElapsedTime(startTimestamp).TotalSeconds,
                    new KeyValuePair<string, object?>("messaging.system", "rabbitmq"),
                    new KeyValuePair<string, object?>("messaging.destination.name", queue));
            };

            TaskCompletionSource channelClosed = new(TaskCreationOptions.RunContinuationsAsynchronously);

            channel.ChannelShutdownAsync += (sender, args) =>
            {
                if (args.Initiator == ShutdownInitiator.Application)
                    channelClosed.TrySetResult();
                else
                    channelClosed.TrySetException(new InvalidOperationException($"Channel shut down unexpectedly: {args.ReplyText}"));

                return Task.CompletedTask;
            };

            await channel.BasicConsumeAsync(queue, false, consumer, cancellationToken)
               .ConfigureAwait(false);

            await channelClosed.Task.WaitAsync(cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
