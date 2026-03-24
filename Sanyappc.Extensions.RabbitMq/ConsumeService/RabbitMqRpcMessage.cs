using System.Diagnostics;
using System.Text;
using System.Text.Json;

using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Sanyappc.Extensions.RabbitMq;

public class RabbitMqRpcMessage
{
    internal const string ErrorHeader = "x-reply-error";

    internal RabbitMqRpcMessage(IChannel channel, BasicDeliverEventArgs @event)
    {
        Channel = channel;
        Event = @event;
    }

    internal IChannel Channel { get; }
    internal BasicDeliverEventArgs Event { get; }
    internal bool Acknowledged { get; private set; }

    public T GetBody<T>(JsonSerializerOptions? options = null)
        where T : notnull
    {
        return RabbitMqMessage.DeserializeBody<T>(Event.Body.Span, options);
    }

    public async Task ReplyAsync(ReadOnlyMemory<byte> body, CancellationToken cancellationToken = default)
    {
        string? replyTo = Event.BasicProperties.ReplyTo;

        if (replyTo is not null)
        {
            BasicProperties properties = new();
            properties.Inject(Activity.Current);

            string? correlationId = Event.BasicProperties.CorrelationId;
            if (correlationId is not null)
                properties.CorrelationId = correlationId;

            await Channel.BasicPublishAsync(string.Empty, replyTo, false, properties, body, cancellationToken)
                .ConfigureAwait(false);
        }

        await Channel.BasicAckAsync(Event.DeliveryTag, false, cancellationToken)
            .ConfigureAwait(false);

        Acknowledged = true;
    }

    public Task ReplyAsync<T>(T body, JsonSerializerOptions? options = null, CancellationToken cancellationToken = default)
    {
        return ReplyAsync(RabbitMqMessage.SerializeBody(body, options), cancellationToken);
    }

    public async Task ReplyErrorAsync(string error, CancellationToken cancellationToken = default)
    {
        string? replyTo = Event.BasicProperties.ReplyTo;

        if (replyTo is not null)
        {
            BasicProperties properties = new();
            properties.Inject(Activity.Current);

            string? correlationId = Event.BasicProperties.CorrelationId;
            if (correlationId is not null)
                properties.CorrelationId = correlationId;

            properties.Headers ??= new Dictionary<string, object?>();
            properties.Headers[ErrorHeader] = Encoding.UTF8.GetBytes(error);

            await Channel.BasicPublishAsync(string.Empty, replyTo, false, properties, ReadOnlyMemory<byte>.Empty, cancellationToken)
                .ConfigureAwait(false);
        }

        await Channel.BasicAckAsync(Event.DeliveryTag, false, cancellationToken)
            .ConfigureAwait(false);

        Acknowledged = true;
    }
}
