using System.Diagnostics;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Sanyappc.Extensions.RabbitMq;

internal partial class RabbitMqConsumeService(ILogger<RabbitMqConsumeService> logger, IRabbitMqChannelFactory rabbitMqChannelFactory, IServiceScopeFactory serviceScopeFactory) : IRabbitMqConsumeService
{
    private readonly ILogger<RabbitMqConsumeService> logger = logger;
    private readonly IRabbitMqChannelFactory rabbitMqChannelFactory = rabbitMqChannelFactory;
    private readonly IServiceScopeFactory serviceScopeFactory = serviceScopeFactory;

    [LoggerMessage(Level = LogLevel.Debug, Message = "Received message from queue {Queue}")]
    private static partial void LogMessageReceived(ILogger logger, string queue);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error processing message from queue {Queue}")]
    private static partial void LogMessageProcessingError(ILogger logger, string queue, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Channel shut down unexpectedly: {Reason}")]
    private static partial void LogChannelShutdown(ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "RabbitMQ broker unavailable while consuming from queue {Queue}")]
    private static partial void LogConsumeFailed(ILogger logger, string queue, Exception exception);

    public async Task ConsumeAsync<T>(string queue, CancellationToken cancellationToken = default)
        where T : class, IRabbitMqMessageProcessingService
    {
        try
        {
            using IChannel channel = await rabbitMqChannelFactory.CreateChannelAsync(cancellationToken)
                .ConfigureAwait(false);

            await channel.QueueDeclareAsync(queue, true, false, false, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            AsyncEventingBasicConsumer consumer = new(channel);
            consumer.ReceivedAsync += async (_, @event) =>
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

                LogMessageReceived(logger, queue);

                RabbitMqTelemetry.ReceivedMessages.Add(1,
                    new KeyValuePair<string, object?>("messaging.system", "rabbitmq"),
                    new KeyValuePair<string, object?>("messaging.destination.name", queue));

                long startTimestamp = Stopwatch.GetTimestamp();

                try
                {
                    await using AsyncServiceScope serviceScope = serviceScopeFactory.CreateAsyncScope();
                    T scopedMessageProcessingService = serviceScope.ServiceProvider.GetRequiredService<T>();

                    await scopedMessageProcessingService.ProcessMessageAsync(new RabbitMqMessage(channel, @event), cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    LogMessageProcessingError(logger, queue, ex);

                    throw;
                }
                finally
                {
                    RabbitMqTelemetry.ProcessDuration.Record(
                        Stopwatch.GetElapsedTime(startTimestamp).TotalSeconds,
                        new KeyValuePair<string, object?>("messaging.system", "rabbitmq"),
                        new KeyValuePair<string, object?>("messaging.destination.name", queue));
                }
            };

            TaskCompletionSource channelClosed = new(TaskCreationOptions.RunContinuationsAsynchronously);

            channel.ChannelShutdownAsync += (_, args) =>
            {
                if (args.Initiator == ShutdownInitiator.Application)
                    channelClosed.TrySetResult();
                else
                {
                    LogChannelShutdown(logger, args.ReplyText);

                    channelClosed.TrySetException(new RabbitMqUnavailableException(
                        $"RabbitMQ channel shut down unexpectedly while consuming from queue '{queue}': {args.ReplyText}"));
                }

                return Task.CompletedTask;
            };

            await channel.BasicConsumeAsync(queue, false, consumer, cancellationToken)
               .ConfigureAwait(false);

            await channelClosed.Task.WaitAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (RabbitMqException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogConsumeFailed(logger, queue, ex);
            RabbitMqTelemetry.FailedMessages.Add(1,
                new KeyValuePair<string, object?>("messaging.system", "rabbitmq"),
                new KeyValuePair<string, object?>("messaging.destination.name", queue),
                new KeyValuePair<string, object?>("error.type", "broker_unavailable"));
            throw new RabbitMqUnavailableException(
                $"RabbitMQ broker is unavailable while consuming from queue '{queue}'.", ex);
        }
    }
}
