using RabbitMQ.Client;

namespace Sanyappc.Extensions.RabbitMq
{
    internal interface IRabbitMqChannelFactory
    {
        ValueTask<IChannel> CreateChannelAsync(CancellationToken cancellationToken = default);
    }
}
