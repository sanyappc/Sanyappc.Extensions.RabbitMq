using System.Diagnostics;
using System.Threading.Channels;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Sanyappc.Extensions.RabbitMq;

file readonly record struct ChannelShutdown(ShutdownEventArgs Reason, bool ConnectionOpen);

internal partial class RabbitMqConsumeService(ILogger<RabbitMqConsumeService> logger, IRabbitMqChannelFactory rabbitMqChannelFactory, IServiceScopeFactory serviceScopeFactory, IOptions<RabbitMqOptions> rabbitMqOptions) : IRabbitMqConsumeService
{
    private readonly ILogger<RabbitMqConsumeService> logger = logger;
    private readonly IRabbitMqChannelFactory rabbitMqChannelFactory = rabbitMqChannelFactory;
    private readonly IServiceScopeFactory serviceScopeFactory = serviceScopeFactory;
    private readonly IOptions<RabbitMqOptions> rabbitMqOptions = rabbitMqOptions;

    [LoggerMessage(EventId = 40, Level = LogLevel.Debug, Message = "Received message from queue {messaging.destination.name}")]
    private static partial void LogMessageReceived(ILogger logger, [TagName("messaging.destination.name")] string queue);

    [LoggerMessage(EventId = 41, Level = LogLevel.Error, Message = "Error processing message from queue {messaging.destination.name}")]
    private static partial void LogMessageProcessingError(ILogger logger, [TagName("messaging.destination.name")] string queue, Exception exception);

    // Information: one line per consumer for a loss the connection already reported as a warning.
    [LoggerMessage(EventId = 42, Level = LogLevel.Information, Message = "Channel shut down unexpectedly: {sanyappc.rabbitmq.shutdown.reason}")]
    private static partial void LogChannelShutdown(ILogger logger, [TagName("sanyappc.rabbitmq.shutdown.reason")] string reason);

    [LoggerMessage(EventId = 43, Level = LogLevel.Error, Message = "RabbitMQ broker unavailable while consuming from queue {messaging.destination.name}")]
    private static partial void LogConsumeFailed(ILogger logger, [TagName("messaging.destination.name")] string queue, Exception exception);

    [LoggerMessage(EventId = 44, Level = LogLevel.Debug, Message = "Received RPC message from queue {messaging.destination.name}")]
    private static partial void LogRpcMessageReceived(ILogger logger, [TagName("messaging.destination.name")] string queue);

    [LoggerMessage(EventId = 45, Level = LogLevel.Error, Message = "Error processing RPC message from queue {messaging.destination.name}")]
    private static partial void LogRpcMessageProcessingError(ILogger logger, [TagName("messaging.destination.name")] string queue, Exception exception);

    [LoggerMessage(EventId = 46, Level = LogLevel.Error, Message = "RabbitMQ broker unavailable while consuming RPC from queue {messaging.destination.name}")]
    private static partial void LogRpcConsumeFailed(ILogger logger, [TagName("messaging.destination.name")] string queue, Exception exception);

    [LoggerMessage(EventId = 47, Level = LogLevel.Debug, Message = "Resumed consuming from queue {messaging.destination.name} after the connection recovered")]
    private static partial void LogConsumeResumed(ILogger logger, [TagName("messaging.destination.name")] string queue);

    private static RabbitMqUnavailableException ChannelShutDownUnexpectedly(string consuming, string queue, ShutdownEventArgs reason) =>
        new($"RabbitMQ channel shut down unexpectedly while {consuming} from queue '{queue}': {reason.ReplyText}");

    private async Task ConsumeUntilClosedAsync(IChannel channel, string queue, string consuming, CancellationToken cancellationToken)
    {
        Channel<ChannelShutdown> shutdowns = Channel.CreateUnbounded<ChannelShutdown>();
        // Never disposed: the client snapshots its handlers before invoking them, so a recovery can still release this after the unsubscribe below.
        SemaphoreSlim recoveries = new(0);
        IRecoverable? recoverable = channel as IRecoverable;

        Task OnShutdownAsync(object? sender, ShutdownEventArgs reason)
        {
            // Read now: by the time the loop sees this shutdown, the recovery may already have reopened the connection.
            shutdowns.Writer.TryWrite(new ChannelShutdown(reason, rabbitMqChannelFactory.IsConnectionOpen));
            return Task.CompletedTask;
        }

        Task OnRecoveryAsync(object? sender, AsyncEventArgs args)
        {
            recoveries.Release();
            return Task.CompletedTask;
        }

        if (recoverable is not null)
            recoverable.RecoveryAsync += OnRecoveryAsync;

        channel.ChannelShutdownAsync += OnShutdownAsync;

        try
        {
            while (true)
            {
                ChannelShutdown shutdown = await shutdowns.Reader.ReadAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (shutdown.Reason.Initiator == ShutdownInitiator.Application)
                    return;

                LogChannelShutdown(logger, shutdown.Reason.ReplyText);

                // The client recovers a channel only together with its connection: one the broker closed on its own stays closed.
                if (shutdown.ConnectionOpen)
                    throw ChannelShutDownUnexpectedly(consuming, queue, shutdown.Reason);

                if (recoverable is null)
                    throw ChannelShutDownUnexpectedly(consuming, queue, shutdown.Reason);

                TimeSpan recoveryTimeout = rabbitMqOptions.Value.RecoveryTimeout;
                bool recovered = await recoveries.WaitAsync(recoveryTimeout, cancellationToken)
                    .ConfigureAwait(false);

                if (!recovered)
                    throw new RabbitMqUnavailableException(
                        $"RabbitMQ connection was not recovered within {recoveryTimeout} after the channel shut down while {consuming} from queue '{queue}': {shutdown.Reason.ReplyText}");

                LogConsumeResumed(logger, queue);
            }
        }
        finally
        {
            channel.ChannelShutdownAsync -= OnShutdownAsync;

            if (recoverable is not null)
                recoverable.RecoveryAsync -= OnRecoveryAsync;
        }
    }

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
                using IDisposable? loggerScope = logger.BeginScope(new MessageLogScope(queue, @event.BasicProperties.MessageId, @event.DeliveryTag));

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

            await channel.BasicConsumeAsync(queue, false, consumer, cancellationToken)
               .ConfigureAwait(false);

            await ConsumeUntilClosedAsync(channel, queue, "consuming", cancellationToken)
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
                using IDisposable? loggerScope = logger.BeginScope(new MessageLogScope(queue, @event.BasicProperties.MessageId, @event.DeliveryTag));

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

            await channel.BasicConsumeAsync(queue, false, consumer, cancellationToken)
               .ConfigureAwait(false);

            await ConsumeUntilClosedAsync(channel, queue, "consuming RPC", cancellationToken)
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
