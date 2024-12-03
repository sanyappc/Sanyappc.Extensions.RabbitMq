namespace Sanyappc.Extensions.RabbitMq
{
    public interface IRabbitMqConsumeService
    {
        ValueTask ConsumeAsync<T>(string queue, CancellationToken cancellationToken = default)
            where T : IRabbitMqMessageProcessingService;
    }
}
