using System.Text.Json;

namespace Sanyappc.Extensions.RabbitMq
{
    public interface IRabbitMqPublishService
    {
        ValueTask PublishAsync(string connectionName, string queue, ReadOnlyMemory<byte> body, CancellationToken cancellationToken = default);

        ValueTask PublishAsync<T>(string connectionName, string queue, T body, JsonSerializerOptions? options = null, CancellationToken cancellationToken = default);

        ValueTask<TOut> PublishAsync<TIn, TOut>(string connectionName, string queue, TIn body, JsonSerializerOptions? options = null, CancellationToken cancellationToken = default);
    }
}
