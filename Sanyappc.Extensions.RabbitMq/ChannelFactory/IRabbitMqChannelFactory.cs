using RabbitMQ.Client;

namespace Sanyappc.Extensions.RabbitMq
{
    public interface IRabbitMqChannelFactory
    {
        ValueTask<IChannel> CreateChannelAsync(string connectionName, CancellationToken cancellationToken = default);
    }
}
