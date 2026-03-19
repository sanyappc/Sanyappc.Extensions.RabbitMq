using RabbitMQ.Client;

namespace Sanyappc.Extensions.RabbitMq;

public interface IRabbitMqChannelFactory
{
    Task<IChannel> CreateChannelAsync(CancellationToken cancellationToken = default);
}
