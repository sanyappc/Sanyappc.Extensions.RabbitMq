namespace Sanyappc.Extensions.RabbitMq
{
    public interface IRabbitMqClientFactory
    {
        public IRabbitMqPublisher BuildPublisher(string connectionName = null);
        public IRabbitMqConsumer BuildConsumer(string connectionName = null);
    }
}
