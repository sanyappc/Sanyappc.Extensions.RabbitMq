namespace Sanyappc.Extensions.RabbitMq.Tests;

internal sealed class DoubleAckingProcessor : IRabbitMqMessageProcessingService
{
    public async Task ProcessMessageAsync(RabbitMqMessage message, CancellationToken cancellationToken = default)
    {
        await message.AckAsync(cancellationToken);

        // The broker answers an unknown delivery tag by closing this channel alone and leaving the connection open.
        await message.AckAsync(cancellationToken);
    }
}
