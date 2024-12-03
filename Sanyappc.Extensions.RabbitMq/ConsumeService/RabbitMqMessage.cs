using System.Text.Json;

using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Sanyappc.Extensions.RabbitMq
{
    public record RabbitMqMessage
    {
        internal RabbitMqMessage(IChannel channel, BasicDeliverEventArgs @event)
        {
            Channel = channel;
            Event = @event;
        }

        public IChannel Channel { get; }

        public BasicDeliverEventArgs Event { get; }

        public T GetBody<T>(JsonSerializerOptions? options = null)
        {
            return DeserializeBody<T>(Event.Body.Span, options);
        }

        public static T DeserializeBody<T>(ReadOnlySpan<byte> message, JsonSerializerOptions? options = null)
        {
            return JsonSerializer.Deserialize<T>(message, options) ?? throw new NotSupportedException();
        }

        public static byte[] SerializeBody<T>(T message, JsonSerializerOptions? options = null)
        {
            return JsonSerializer.SerializeToUtf8Bytes(message, options);
        }
    }
}
