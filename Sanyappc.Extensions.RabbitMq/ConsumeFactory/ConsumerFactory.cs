using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Sanyappc.Extensions.RabbitMq
{
    // Здесь (и везде) лучше инъектить IServiceProvider или IServiceScopeFactory?
    internal class ConsumerFactory(
        IServiceProvider serviceProvider,
        IOptions<RabbitMqOptions> options) : IConsumerFactory
    {
        protected readonly IServiceProvider serviceProvider = serviceProvider;

        public TConsumer Build<TConsumer>(string consumerName) where TConsumer : IRabbitConsumer
        {
            if (!options.Value.Consumers.TryGetValue(consumerName, out RabbitMqConsumerOptions? consumerOptions))
                throw new KeyNotFoundException($"No Consumer config named '{consumerName}'");

            ILoggerFactory loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
            ILogger<TConsumer> logger = loggerFactory.CreateLogger<TConsumer>();
            IRabbitMqChannelFactory chfactory = serviceProvider.GetRequiredService<IRabbitMqChannelFactory>();

            return (TConsumer)Activator.CreateInstance(typeof(TConsumer), logger, chfactory, serviceProvider, consumerOptions.ConnectionName, consumerOptions.QueueName)!;
        }
    }
}
