using System.Text.Json;

namespace Sanyappc.Extensions.RabbitMq
{
    public interface IRabbitMqPublisher
    {
        Task PublishAsync(ReadOnlyMemory<byte> body, CancellationToken cancellationToken = default);

        Task PublishAsync<T>(T body, JsonSerializerOptions? options = null, CancellationToken cancellationToken = default);

        Task<TOut> PublishAsync<TIn, TOut>(TIn body, JsonSerializerOptions? options = null, CancellationToken cancellationToken = default);
    }
}
