namespace Sanyappc.Extensions.RabbitMq;

public interface IRabbitMqRpcMessageProcessingService
{
    Task ProcessMessageAsync(RabbitMqRpcMessage message, CancellationToken cancellationToken = default);
}
