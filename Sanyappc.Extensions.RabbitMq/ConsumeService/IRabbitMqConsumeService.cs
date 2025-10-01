namespace Sanyappc.Extensions.RabbitMq
{
    public interface IRabbitMqConsumeService
    {
        ValueTask ConsumeAsync<T>(CancellationToken cancellationToken = default)
            where T : IRabbitMqMessageProcessingService;
    }
}
