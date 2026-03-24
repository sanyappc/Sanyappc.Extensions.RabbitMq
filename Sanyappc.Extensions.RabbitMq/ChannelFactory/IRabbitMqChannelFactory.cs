using RabbitMQ.Client;

namespace Sanyappc.Extensions.RabbitMq;

public interface IRabbitMqChannelFactory
{
    Task CheckAsync(CancellationToken cancellationToken = default);

    Task<IChannel> CreateChannelAsync(CancellationToken cancellationToken = default);
}
