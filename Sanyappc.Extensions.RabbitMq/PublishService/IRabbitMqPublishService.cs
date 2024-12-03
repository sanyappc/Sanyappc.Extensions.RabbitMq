using System.Text.Json;

namespace Sanyappc.Extensions.RabbitMq
{
    public interface IRabbitMqPublishService
    {
        ValueTask PublishAsync(string queue, ReadOnlyMemory<byte> body, CancellationToken cancellationToken = default);

        ValueTask PublishAsync<T>(string queue, T body, JsonSerializerOptions? options = null, CancellationToken cancellationToken = default);

        ValueTask<TOut> PublishAsync<TIn, TOut>(string queue, TIn body, JsonSerializerOptions? options = null, CancellationToken cancellationToken = default);
    }
}
