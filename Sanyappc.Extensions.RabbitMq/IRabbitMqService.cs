namespace Sanyappc.Extensions.RabbitMq
{
    public interface IRabbitMqService
    {
        ValueTask PublishAsync<T>(string queue, T message, CancellationToken cancellationToken = default);

        ValueTask<TOut> PublishAsync<TIn, TOut>(string queue, TIn message, CancellationToken cancellationToken = default);

        IAsyncEnumerable<RabbitMqMessage<T>> ConsumeAsync<T>(string queue, CancellationToken cancellationToken = default);
    }
}
