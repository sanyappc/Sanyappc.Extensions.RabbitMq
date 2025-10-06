using Sanyappc.Extensions.RabbitMq;

namespace Amadesci.Extensions.NamedRabbitMq.PublishFactory
{
    public interface IPublisherFactory
    {
        Task<IRabbitMqPublisher> BuildAsync(CancellationToken cancellationToken, string connectionName = null);
    }
}
