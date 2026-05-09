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

    [LoggerMessage(Level = LogLevel.Debug, Message = "Publishing message to queue {Queue}")]
    private static partial void LogPublish(ILogger logger, string queue);

    [LoggerMessage(Level = LogLevel.Error, Message = "RabbitMQ broker unavailable while publishing to queue {Queue}")]
    private static partial void LogPublishFailed(ILogger logger, string queue, Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Sending request to queue {Queue}, awaiting reply")]
    private static partial void LogRequest(ILogger logger, string queue);

    [LoggerMessage(Level = LogLevel.Warning, Message = "RabbitMQ request to queue {Queue} timed out after {TimeoutSeconds} seconds")]
    private static partial void LogRequestTimedOut(ILogger logger, string queue, int timeoutSeconds);

    [LoggerMessage(Level = LogLevel.Error, Message = "RabbitMQ broker unavailable during request to queue {Queue}")]
    private static partial void LogRequestFailed(ILogger logger, string queue, Exception exception);

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

            int replyTimeoutInSeconds = rabbitMqOptions.Value.ReplyTimeoutInSeconds;
            if (replyTimeoutInSeconds != Timeout.Infinite)
            {
                using CancellationTokenSource timeoutCancellationTokenSource = new();
                timeoutCancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(replyTimeoutInSeconds));

                using CancellationTokenSource linkedCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    timeoutCancellationTokenSource.Token);

                try
                {
                    await replyTaskCompletionSource.Task.WaitAsync(linkedCancellationTokenSource.Token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException ex) when (ex.CancellationToken == timeoutCancellationTokenSource.Token)
                {
                    LogRequestTimedOut(logger, queue, replyTimeoutInSeconds);
                    throw new RabbitMqTimeoutException(
                        $"The RabbitMQ request to queue '{queue}' timed out after {replyTimeoutInSeconds} seconds.");
                }
            }
            else
            {
                await replyTaskCompletionSource.Task.WaitAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
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

            int replyTimeoutInSeconds = rabbitMqOptions.Value.ReplyTimeoutInSeconds;
            if (replyTimeoutInSeconds != Timeout.Infinite)
            {
                using CancellationTokenSource timeoutCancellationTokenSource = new();
                timeoutCancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(replyTimeoutInSeconds));

                using CancellationTokenSource linkedCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    timeoutCancellationTokenSource.Token);

                try
                {
                    return await replyTaskCompletionSource.Task.WaitAsync(linkedCancellationTokenSource.Token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException ex) when (ex.CancellationToken == timeoutCancellationTokenSource.Token)
                {
                    LogRequestTimedOut(logger, queue, replyTimeoutInSeconds);
                    throw new RabbitMqTimeoutException(
                        $"The RabbitMQ request to queue '{queue}' timed out after {replyTimeoutInSeconds} seconds.");
                }
            }
            else
            {
                return await replyTaskCompletionSource.Task.WaitAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
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
