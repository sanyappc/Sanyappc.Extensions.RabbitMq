namespace Sanyappc.Extensions.RabbitMq;

public interface IRabbitMqConsumeService
{
    Task ConsumeAsync<T>(string queue, CancellationToken cancellationToken = default)
        where T : class, IRabbitMqMessageProcessingService;

    Task ConsumeRpcAsync<T>(string queue, CancellationToken cancellationToken = default)
        where T : class, IRabbitMqRpcMessageProcessingService;
}
