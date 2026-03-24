using RabbitMQ.Client;

namespace Sanyappc.Extensions.RabbitMq;

public static class RabbitMqMessageExtensions
{
    public static async Task AckAsync(this RabbitMqMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        await message.Channel.BasicAckAsync(message.Event.DeliveryTag, false, cancellationToken)
            .ConfigureAwait(false);
    }

    public static async Task RejectAsync(this RabbitMqMessage message, bool requeue, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        await message.Channel.BasicRejectAsync(message.Event.DeliveryTag, requeue, cancellationToken)
            .ConfigureAwait(false);
    }
}
