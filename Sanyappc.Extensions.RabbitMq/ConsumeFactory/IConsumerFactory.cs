namespace Sanyappc.Extensions.RabbitMq
{
    public interface IConsumerFactory
    {
        public TConsumer Build<TConsumer>(string consumerName) where TConsumer : IRabbitConsumer;
    }
}
