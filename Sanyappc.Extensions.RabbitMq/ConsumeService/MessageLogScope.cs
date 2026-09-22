using System.Collections;

namespace Sanyappc.Extensions.RabbitMq;

// The OTLP exporter reads an IReadOnlyList<KeyValuePair> scope as attributes; the simple console prints ToString.
internal sealed class MessageLogScope(string queue, string? messageId, ulong deliveryTag) : IReadOnlyList<KeyValuePair<string, object?>>
{
    public int Count => 3;

    public KeyValuePair<string, object?> this[int index] => index switch
    {
        0 => new(RabbitMqTelemetry.DestinationNameTag, queue),
        1 => new(RabbitMqTelemetry.MessageIdTag, messageId),
        2 => new(RabbitMqTelemetry.DeliveryTagTag, deliveryTag),
        _ => throw new ArgumentOutOfRangeException(nameof(index), index, "A message scope carries three tags")
    };

    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
    {
        for (int index = 0; index < Count; index++)
            yield return this[index];
    }

    public override string ToString() => $"queue {queue}, message {messageId ?? "-"}, delivery tag {deliveryTag}";

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
