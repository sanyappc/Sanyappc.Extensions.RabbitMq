using System.Text.Json;

using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Sanyappc.Extensions.RabbitMq;

public class RabbitMqMessage
{
    internal RabbitMqMessage(IChannel channel, BasicDeliverEventArgs @event)
    {
        Channel = channel;
        Event = @event;
    }

    internal IChannel Channel { get; }

    public BasicDeliverEventArgs Event { get; }

    public T GetBody<T>(JsonSerializerOptions? options = null)
        where T : notnull
    {
        return DeserializeBody<T>(Event.Body.Span, options);
    }

    public Task AckAsync(CancellationToken cancellationToken = default)
    {
        return Channel.BasicAckAsync(Event.DeliveryTag, false, cancellationToken).AsTask();
    }

    public Task RejectAsync(bool requeue, CancellationToken cancellationToken = default)
    {
        return Channel.BasicRejectAsync(Event.DeliveryTag, requeue, cancellationToken).AsTask();
    }

    public static T DeserializeBody<T>(ReadOnlySpan<byte> message, JsonSerializerOptions? options = null)
        where T : notnull
    {
        return JsonSerializer.Deserialize<T>(message, options) ?? throw new InvalidOperationException("Deserialized value is null.");
    }

    public static byte[] SerializeBody<T>(T message, JsonSerializerOptions? options = null)
    {
        return JsonSerializer.SerializeToUtf8Bytes(message, options);
    }
}
