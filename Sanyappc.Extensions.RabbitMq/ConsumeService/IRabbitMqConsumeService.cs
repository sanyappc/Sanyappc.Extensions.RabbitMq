namespace Sanyappc.Extensions.RabbitMq
{
    public interface IRabbitMqConsumeService
    {
        Task ConsumeAsync<T>(string queue, CancellationToken cancellationToken = default)
            where T : IRabbitMqMessageProcessingService;
    }
}
