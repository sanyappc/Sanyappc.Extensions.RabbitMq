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

    [LoggerMessage(Level = LogLevel.Debug, Message = "Received RPC message from queue {Queue}")]
    private static partial void LogRpcMessageReceived(ILogger logger, string queue);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error processing RPC message from queue {Queue}")]
    private static partial void LogRpcMessageProcessingError(ILogger logger, string queue, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "RabbitMQ broker unavailable while consuming RPC from queue {Queue}")]
    private static partial void LogRpcConsumeFailed(ILogger logger, string queue, Exception exception);

    public async Task ConsumeAsync<T>(string queue, CancellationToken cancellationToken = default)
        where T : class, IRabbitMqMessageProcessingService
    {
        string serverAddress = rabbitMqChannelFactory.ServerAddress;
        int serverPort = rabbitMqChannelFactory.ServerPort;

        try
        {
            using IChannel channel = await rabbitMqChannelFactory.CreateChannelAsync(cancellationToken)
                .ConfigureAwait(false);

            await channel.QueueDeclareAsync(queue, true, false, false, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            AsyncEventingBasicConsumer consumer = new(channel);
            consumer.ReceivedAsync += async (_, @event) =>
            {
                using Activity? activity = @event.StartProcessActivity(queue, serverAddress, serverPort);
                using IDisposable? loggerScope = logger.BeginScope(new Dictionary<string, object?>
                {
                    ["Queue"] = queue,
                    ["MessageId"] = @event.BasicProperties.MessageId,
                    ["DeliveryTag"] = @event.DeliveryTag,
                    ["TraceId"] = activity?.TraceId.ToString(),
                    ["SpanId"] = activity?.SpanId.ToString(),
                });

                LogMessageReceived(logger, queue);

                RabbitMqTelemetry.IncrementConsumed(queue, serverAddress, serverPort);

                long startTimestamp = Stopwatch.GetTimestamp();
                string? errorType = null;

                try
                {
                    await using AsyncServiceScope serviceScope = serviceScopeFactory.CreateAsyncScope();
                    T scopedMessageProcessingService = serviceScope.ServiceProvider.GetRequiredService<T>();

                    await scopedMessageProcessingService.ProcessMessageAsync(new RabbitMqMessage(channel, @event), cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    errorType = RabbitMqTelemetry.GetErrorType(ex);
                    activity.SetError(ex, errorType);
                    LogMessageProcessingError(logger, queue, ex);

                    throw;
                }
                finally
                {
                    RabbitMqTelemetry.RecordProcessDuration(queue, serverAddress, serverPort, startTimestamp, errorType);
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
            throw new RabbitMqUnavailableException(
                $"RabbitMQ broker is unavailable while consuming from queue '{queue}'.", ex);
        }
    }

    public async Task ConsumeRpcAsync<T>(string queue, CancellationToken cancellationToken = default)
        where T : class, IRabbitMqRpcMessageProcessingService
    {
        string serverAddress = rabbitMqChannelFactory.ServerAddress;
        int serverPort = rabbitMqChannelFactory.ServerPort;

        try
        {
            using IChannel channel = await rabbitMqChannelFactory.CreateChannelAsync(cancellationToken)
                .ConfigureAwait(false);

            await channel.QueueDeclareAsync(queue, true, false, false, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            AsyncEventingBasicConsumer consumer = new(channel);
            consumer.ReceivedAsync += async (_, @event) =>
            {
                using Activity? activity = @event.StartProcessActivity(queue, serverAddress, serverPort);
                using IDisposable? loggerScope = logger.BeginScope(new Dictionary<string, object?>
                {
                    ["Queue"] = queue,
                    ["MessageId"] = @event.BasicProperties.MessageId,
                    ["DeliveryTag"] = @event.DeliveryTag,
                    ["TraceId"] = activity?.TraceId.ToString(),
                    ["SpanId"] = activity?.SpanId.ToString(),
                });

                LogRpcMessageReceived(logger, queue);

                RabbitMqTelemetry.IncrementConsumed(queue, serverAddress, serverPort);

                RabbitMqRpcMessage rpcMessage = new(channel, @event);
                long startTimestamp = Stopwatch.GetTimestamp();
                string? errorType = null;

                try
                {
                    await using AsyncServiceScope serviceScope = serviceScopeFactory.CreateAsyncScope();
                    T scopedMessageProcessingService = serviceScope.ServiceProvider.GetRequiredService<T>();

                    await scopedMessageProcessingService.ProcessMessageAsync(rpcMessage, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    errorType = RabbitMqTelemetry.GetErrorType(ex);
                    activity.SetError(ex, errorType);
                    LogRpcMessageProcessingError(logger, queue, ex);

                    if (!rpcMessage.Acknowledged)
                    {
                        try
                        {
                            await rpcMessage.ReplyErrorAsync(ex.Message, cancellationToken).ConfigureAwait(false);
                        }
                        catch
                        {
                            // best effort — original exception is the one that matters
                        }
                    }

                    throw;
                }
                finally
                {
                    RabbitMqTelemetry.RecordProcessDuration(queue, serverAddress, serverPort, startTimestamp, errorType);
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
                        $"RabbitMQ channel shut down unexpectedly while consuming RPC from queue '{queue}': {args.ReplyText}"));
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
            LogRpcConsumeFailed(logger, queue, ex);
            throw new RabbitMqUnavailableException(
                $"RabbitMQ broker is unavailable while consuming RPC from queue '{queue}'.", ex);
        }
    }
}
