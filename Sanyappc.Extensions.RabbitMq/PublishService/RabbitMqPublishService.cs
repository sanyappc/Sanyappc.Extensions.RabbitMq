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

        using Activity? activity = RabbitMqBasicPropertiesExtensions.StartPublishActivity(queue);

        try
        {
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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            LogPublishFailed(logger, queue, ex);
            RabbitMqTelemetry.FailedMessages.Add(1,
                new KeyValuePair<string, object?>("messaging.system", "rabbitmq"),
                new KeyValuePair<string, object?>("messaging.destination.name", queue),
                new KeyValuePair<string, object?>("error.type", "broker_unavailable"));
            throw new RabbitMqUnavailableException(
                $"RabbitMQ broker is unavailable while publishing to queue '{queue}'.", ex);
        }
    }

    public Task PublishAsync<T>(string queue, T body, JsonSerializerOptions? options = null, CancellationToken cancellationToken = default)
    {
        return PublishAsync(queue, RabbitMqMessage.SerializeBody(body, options), cancellationToken);
    }

    public async Task RequestAsync<TIn>(string queue, TIn body, JsonSerializerOptions? options = null, CancellationToken cancellationToken = default)
    {
        LogRequest(logger, queue);

        using Activity? activity = RabbitMqBasicPropertiesExtensions.StartRequestActivity(queue);

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

            await channel.BasicPublishAsync(string.Empty, queue, false, properties, RabbitMqMessage.SerializeBody(body, options), cancellationToken)
                .ConfigureAwait(false);

            RabbitMqTelemetry.PublishedMessages.Add(1,
                new KeyValuePair<string, object?>("messaging.system", "rabbitmq"),
                new KeyValuePair<string, object?>("messaging.destination.name", queue));

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
                    activity?.SetStatus(ActivityStatusCode.Error, "Request timed out.");
                    LogRequestTimedOut(logger, queue, replyTimeoutInSeconds);
                    RabbitMqTelemetry.FailedMessages.Add(1,
                        new KeyValuePair<string, object?>("messaging.system", "rabbitmq"),
                        new KeyValuePair<string, object?>("messaging.destination.name", queue),
                        new KeyValuePair<string, object?>("error.type", "timeout"));
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
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            LogRequestFailed(logger, queue, ex);
            RabbitMqTelemetry.FailedMessages.Add(1,
                new KeyValuePair<string, object?>("messaging.system", "rabbitmq"),
                new KeyValuePair<string, object?>("messaging.destination.name", queue),
                new KeyValuePair<string, object?>("error.type", "broker_unavailable"));
            throw new RabbitMqUnavailableException(
                $"RabbitMQ broker is unavailable during a request to queue '{queue}'.", ex);
        }
    }

    public async Task<TOut> RequestAsync<TIn, TOut>(string queue, TIn body, JsonSerializerOptions? options = null, CancellationToken cancellationToken = default)
        where TOut : notnull
    {
        LogRequest(logger, queue);

        using Activity? activity = RabbitMqBasicPropertiesExtensions.StartRequestActivity(queue);

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

            await channel.BasicPublishAsync(string.Empty, queue, false, properties, RabbitMqMessage.SerializeBody(body, options), cancellationToken)
                .ConfigureAwait(false);

            RabbitMqTelemetry.PublishedMessages.Add(1,
                new KeyValuePair<string, object?>("messaging.system", "rabbitmq"),
                new KeyValuePair<string, object?>("messaging.destination.name", queue));

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
                    activity?.SetStatus(ActivityStatusCode.Error, "Request timed out.");
                    LogRequestTimedOut(logger, queue, replyTimeoutInSeconds);
                    RabbitMqTelemetry.FailedMessages.Add(1,
                        new KeyValuePair<string, object?>("messaging.system", "rabbitmq"),
                        new KeyValuePair<string, object?>("messaging.destination.name", queue),
                        new KeyValuePair<string, object?>("error.type", "timeout"));
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
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            LogRequestFailed(logger, queue, ex);
            RabbitMqTelemetry.FailedMessages.Add(1,
                new KeyValuePair<string, object?>("messaging.system", "rabbitmq"),
                new KeyValuePair<string, object?>("messaging.destination.name", queue),
                new KeyValuePair<string, object?>("error.type", "broker_unavailable"));
            throw new RabbitMqUnavailableException(
                $"RabbitMQ broker is unavailable during a request to queue '{queue}'.", ex);
        }
    }
}
