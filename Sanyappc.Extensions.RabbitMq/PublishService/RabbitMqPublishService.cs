using System.Diagnostics;
using System.Text;
using System.Text.Json;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Sanyappc.Extensions.RabbitMq;

internal partial class RabbitMqPublishService(ILogger<RabbitMqPublishService> logger, IRabbitMqChannelFactory rabbitMqChannelFactory, IOptions<RabbitMqOptions> rabbitMqOptions) : IRabbitMqPublishService
{
    private const string replyToQueue = "amq.rabbitmq.reply-to";

    private readonly ILogger<RabbitMqPublishService> logger = logger;
    private readonly IRabbitMqChannelFactory rabbitMqChannelFactory = rabbitMqChannelFactory;
    private readonly IOptions<RabbitMqOptions> rabbitMqOptions = rabbitMqOptions;

    [LoggerMessage(EventId = 20, Level = LogLevel.Debug, Message = "Publishing message to queue {messaging.destination.name}")]
    private static partial void LogPublish(ILogger logger, [TagName("messaging.destination.name")] string queue);

    [LoggerMessage(EventId = 21, Level = LogLevel.Error, Message = "RabbitMQ broker unavailable while publishing to queue {messaging.destination.name}")]
    private static partial void LogPublishFailed(ILogger logger, [TagName("messaging.destination.name")] string queue, Exception exception);

    [LoggerMessage(EventId = 22, Level = LogLevel.Debug, Message = "Sending request to queue {messaging.destination.name}, awaiting reply")]
    private static partial void LogRequest(ILogger logger, [TagName("messaging.destination.name")] string queue);

    [LoggerMessage(EventId = 23, Level = LogLevel.Warning, Message = "RabbitMQ request to queue {messaging.destination.name} timed out after {sanyappc.rabbitmq.reply.timeout}")]
    private static partial void LogRequestTimedOut(ILogger logger, [TagName("messaging.destination.name")] string queue, [TagName("sanyappc.rabbitmq.reply.timeout")] TimeSpan timeout);

    [LoggerMessage(EventId = 24, Level = LogLevel.Error, Message = "RabbitMQ broker unavailable during request to queue {messaging.destination.name}")]
    private static partial void LogRequestFailed(ILogger logger, [TagName("messaging.destination.name")] string queue, Exception exception);

    private async Task AwaitReplyAsync(Task reply, string queue, CancellationToken cancellationToken)
    {
        TimeSpan replyTimeout = rabbitMqOptions.Value.ReplyTimeout;
        if (replyTimeout == Timeout.InfiniteTimeSpan)
        {
            await reply.WaitAsync(cancellationToken)
                .ConfigureAwait(false);

            return;
        }

        using CancellationTokenSource timeoutCancellationTokenSource = new();
        timeoutCancellationTokenSource.CancelAfter(replyTimeout);

        using CancellationTokenSource linkedCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCancellationTokenSource.Token);

        try
        {
            await reply.WaitAsync(linkedCancellationTokenSource.Token)
                .ConfigureAwait(false);
        }
        // WaitAsync cancels with the linked token, never with the timeout's own, so the timeout source is what to ask.
        catch (OperationCanceledException) when (timeoutCancellationTokenSource.IsCancellationRequested)
        {
            if (cancellationToken.IsCancellationRequested)
                throw;

            LogRequestTimedOut(logger, queue, replyTimeout);
            throw new RabbitMqTimeoutException(
                $"The RabbitMQ request to queue '{queue}' timed out after {replyTimeout}.");
        }
    }

    public async Task PublishAsync(string queue, ReadOnlyMemory<byte> body, CancellationToken cancellationToken = default)
    {
        LogPublish(logger, queue);

        string serverAddress = rabbitMqChannelFactory.ServerAddress;
        int serverPort = rabbitMqChannelFactory.ServerPort;

        using Activity? activity = RabbitMqBasicPropertiesExtensions.StartPublishActivity(queue, serverAddress, serverPort, body.Length);
        long startTimestamp = Stopwatch.GetTimestamp();
        string? errorType = null;
        bool publishAttempted = false;

        try
        {
            using IChannel channel = await rabbitMqChannelFactory.CreateChannelAsync(cancellationToken)
                .ConfigureAwait(false);

            await channel.QueueDeclareAsync(queue, true, false, false, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            BasicProperties properties = new();
            properties.Inject(Activity.Current);

            publishAttempted = true;
            await channel.BasicPublishAsync(string.Empty, queue, false, properties, body, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            errorType = RabbitMqTelemetry.BrokerUnavailableError;
            activity.SetError(ex, errorType);
            LogPublishFailed(logger, queue, ex);
            throw new RabbitMqUnavailableException(
                $"RabbitMQ broker is unavailable while publishing to queue '{queue}'.", ex);
        }
        finally
        {
            if (publishAttempted)
                RabbitMqTelemetry.IncrementSent(queue, serverAddress, serverPort, errorType);

            RabbitMqTelemetry.RecordSendDuration(queue, serverAddress, serverPort, startTimestamp, errorType);
        }
    }

    public Task PublishAsync<T>(string queue, T body, JsonSerializerOptions? options = null, CancellationToken cancellationToken = default)
    {
        return PublishAsync(queue, RabbitMqMessage.SerializeBody(body, options), cancellationToken);
    }

    public async Task RequestAsync<TIn>(string queue, TIn body, JsonSerializerOptions? options = null, CancellationToken cancellationToken = default)
    {
        LogRequest(logger, queue);

        byte[] serializedBody = RabbitMqMessage.SerializeBody(body, options);

        string serverAddress = rabbitMqChannelFactory.ServerAddress;
        int serverPort = rabbitMqChannelFactory.ServerPort;

        using Activity? activity = RabbitMqBasicPropertiesExtensions.StartRequestActivity(queue, serverAddress, serverPort, serializedBody.Length);
        long startTimestamp = Stopwatch.GetTimestamp();
        string? errorType = null;
        bool publishAttempted = false;

        try
        {
            using IChannel channel = await rabbitMqChannelFactory.CreateChannelAsync(cancellationToken)
                .ConfigureAwait(false);

            TaskCompletionSource replyTaskCompletionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);

            AsyncEventingBasicConsumer consumer = new(channel);
            consumer.ReceivedAsync += (_, @event) =>
            {
                try
                {
                    if (@event.BasicProperties.Headers?.TryGetValue(RabbitMqRpcMessage.ErrorHeader, out object? errorObj) == true)
                    {
                        string error = errorObj is byte[] bytes
                            ? Encoding.UTF8.GetString(bytes)
                            : errorObj?.ToString() ?? string.Empty;
                        replyTaskCompletionSource.TrySetException(new RabbitMqRequestRejectedException(error));
                    }
                    else
                    {
                        replyTaskCompletionSource.TrySetResult();
                    }
                }
                catch (Exception ex)
                {
                    replyTaskCompletionSource.TrySetException(ex);
                }

                return Task.CompletedTask;
            };

            await channel.BasicConsumeAsync(replyToQueue, true, consumer, cancellationToken)
                .ConfigureAwait(false);

            await channel.QueueDeclareAsync(queue, true, false, false, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            BasicProperties properties = new();
            properties.Inject(Activity.Current);
            properties.ReplyTo = replyToQueue;

            publishAttempted = true;
            await channel.BasicPublishAsync(string.Empty, queue, false, properties, serializedBody, cancellationToken)
                .ConfigureAwait(false);

            await AwaitReplyAsync(replyTaskCompletionSource.Task, queue, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (RabbitMqException ex)
        {
            errorType = RabbitMqTelemetry.GetErrorType(ex);
            activity.SetError(ex, errorType);
            throw;
        }
        catch (Exception ex)
        {
            errorType = RabbitMqTelemetry.BrokerUnavailableError;
            activity.SetError(ex, errorType);
            LogRequestFailed(logger, queue, ex);
            throw new RabbitMqUnavailableException(
                $"RabbitMQ broker is unavailable during a request to queue '{queue}'.", ex);
        }
        finally
        {
            if (publishAttempted)
                RabbitMqTelemetry.IncrementSent(queue, serverAddress, serverPort, errorType);

            RabbitMqTelemetry.RecordSendDuration(queue, serverAddress, serverPort, startTimestamp, errorType);
        }
    }

    public async Task<TOut> RequestAsync<TIn, TOut>(string queue, TIn body, JsonSerializerOptions? options = null, CancellationToken cancellationToken = default)
        where TOut : notnull
    {
        LogRequest(logger, queue);

        byte[] serializedBody = RabbitMqMessage.SerializeBody(body, options);

        string serverAddress = rabbitMqChannelFactory.ServerAddress;
        int serverPort = rabbitMqChannelFactory.ServerPort;

        using Activity? activity = RabbitMqBasicPropertiesExtensions.StartRequestActivity(queue, serverAddress, serverPort, serializedBody.Length);
        long startTimestamp = Stopwatch.GetTimestamp();
        string? errorType = null;
        bool publishAttempted = false;

        try
        {
            using IChannel channel = await rabbitMqChannelFactory.CreateChannelAsync(cancellationToken)
                .ConfigureAwait(false);

            TaskCompletionSource<TOut> replyTaskCompletionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);

            AsyncEventingBasicConsumer consumer = new(channel);
            consumer.ReceivedAsync += (_, @event) =>
            {
                try
                {
                    if (@event.BasicProperties.Headers?.TryGetValue(RabbitMqRpcMessage.ErrorHeader, out object? errorObj) == true)
                    {
                        string error = errorObj is byte[] bytes
                            ? Encoding.UTF8.GetString(bytes)
                            : errorObj?.ToString() ?? string.Empty;
                        replyTaskCompletionSource.TrySetException(new RabbitMqRequestRejectedException(error));
                    }
                    else
                    {
                        replyTaskCompletionSource.TrySetResult(RabbitMqMessage.DeserializeBody<TOut>(@event.Body.Span, options));
                    }
                }
                catch (Exception ex)
                {
                    replyTaskCompletionSource.TrySetException(ex);
                }

                return Task.CompletedTask;
            };

            await channel.BasicConsumeAsync(replyToQueue, true, consumer, cancellationToken)
                .ConfigureAwait(false);

            await channel.QueueDeclareAsync(queue, true, false, false, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            BasicProperties properties = new();
            properties.Inject(Activity.Current);
            properties.ReplyTo = replyToQueue;

            publishAttempted = true;
            await channel.BasicPublishAsync(string.Empty, queue, false, properties, serializedBody, cancellationToken)
                .ConfigureAwait(false);

            await AwaitReplyAsync(replyTaskCompletionSource.Task, queue, cancellationToken)
                .ConfigureAwait(false);

            return await replyTaskCompletionSource.Task
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (RabbitMqException ex)
        {
            errorType = RabbitMqTelemetry.GetErrorType(ex);
            activity.SetError(ex, errorType);
            throw;
        }
        catch (Exception ex)
        {
            errorType = RabbitMqTelemetry.BrokerUnavailableError;
            activity.SetError(ex, errorType);
            LogRequestFailed(logger, queue, ex);
            throw new RabbitMqUnavailableException(
                $"RabbitMQ broker is unavailable during a request to queue '{queue}'.", ex);
        }
        finally
        {
            if (publishAttempted)
                RabbitMqTelemetry.IncrementSent(queue, serverAddress, serverPort, errorType);

            RabbitMqTelemetry.RecordSendDuration(queue, serverAddress, serverPort, startTimestamp, errorType);
        }
    }
}
