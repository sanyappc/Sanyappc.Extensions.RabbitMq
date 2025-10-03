using System.Text.Json;

namespace Sanyappc.Extensions.RabbitMq
{
    public interface IRabbitMqPublisher
    {
        ValueTask PublishAsync(ReadOnlyMemory<byte> body, CancellationToken cancellationToken = default);

        ValueTask PublishAsync<T>(T body, JsonSerializerOptions? options = null, CancellationToken cancellationToken = default);

        ValueTask<TOut> PublishAsync<TIn, TOut>(TIn body, JsonSerializerOptions? options = null, CancellationToken cancellationToken = default);
    }
}
