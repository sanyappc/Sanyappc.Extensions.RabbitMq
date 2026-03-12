namespace Sanyappc.Extensions.RabbitMq
{
    public interface IRabbitMqMessageProcessingService
    {
        Task ProcessMessageAsync(RabbitMqMessage message, CancellationToken cancellationToken = default);
    }
}
