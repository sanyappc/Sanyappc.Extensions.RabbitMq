using Sanyappc.Extensions.RabbitMq.ConsumeFactory;

namespace Sanyappc.Extensions.RabbitMq
{
    public interface IConsumerFactory
    {
        public Task<RabbitMqConsumer> BuildAsync(string consumerName, CancellationToken cancellationToken);
    }
}
