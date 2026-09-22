namespace Sanyappc.Extensions.RabbitMq.Tests;

internal sealed class InboxProcessor(Inbox inbox) : IRabbitMqMessageProcessingService
{
    public async Task ProcessMessageAsync(RabbitMqMessage message, CancellationToken cancellationToken = default)
    {
        await message.AckAsync(cancellationToken);
        inbox.Deliver(message.GetBody<string>());
    }
}
