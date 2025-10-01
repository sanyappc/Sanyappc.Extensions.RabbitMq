namespace Sanyappc.Extensions.RabbitMq
{
    public interface IRabbitConsumer
    {
        Task ExecuteAsync<TService>(CancellationToken cancelllationToke) where TService : IRabbitMqMessageProcessingService;
    }
}
