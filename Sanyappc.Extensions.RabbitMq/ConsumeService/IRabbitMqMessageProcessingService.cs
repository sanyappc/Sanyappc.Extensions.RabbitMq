namespace Sanyappc.Extensions.RabbitMq
{
    public interface IRabbitMqMessageProcessingService
    {
        ValueTask ProcessMessageAsync(RabbitMqMessage message, CancellationToken cancellationToken = default);
    }
}
