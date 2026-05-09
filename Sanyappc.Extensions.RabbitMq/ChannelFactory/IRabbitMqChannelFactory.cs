using RabbitMQ.Client;

namespace Sanyappc.Extensions.RabbitMq;

public interface IRabbitMqChannelFactory
{
    string ServerAddress { get; }

    int ServerPort { get; }

    Task CheckAsync(CancellationToken cancellationToken = default);

    Task<IChannel> CreateChannelAsync(CancellationToken cancellationToken = default);
}
