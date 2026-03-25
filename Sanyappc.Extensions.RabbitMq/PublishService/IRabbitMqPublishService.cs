using System.Text.Json;

namespace Sanyappc.Extensions.RabbitMq;

public interface IRabbitMqPublishService
{
    Task PublishAsync(string queue, ReadOnlyMemory<byte> body, CancellationToken cancellationToken = default);

    Task PublishAsync<T>(string queue, T body, JsonSerializerOptions? options = null, CancellationToken cancellationToken = default);

    Task RequestAsync<TIn>(string queue, TIn body, JsonSerializerOptions? options = null, CancellationToken cancellationToken = default);

    Task<TOut> RequestAsync<TIn, TOut>(string queue, TIn body, JsonSerializerOptions? options = null, CancellationToken cancellationToken = default)
        where TOut : notnull;
}
