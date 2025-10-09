namespace Sanyappc.Extensions.RabbitMq
{
    public interface IRabbitMqConsumer
    {
        Task ConsumeAsync<TService>(CancellationToken cancelllationToke) where TService : IRabbitMqMessageProcessingService;
    }
}
