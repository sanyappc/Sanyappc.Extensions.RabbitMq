using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Sanyappc.Extensions.RabbitMq.ConsumeFactory;

namespace Sanyappc.Extensions.RabbitMq
{
    internal class ConsumerFactory(
        IServiceProvider serviceProvider,
        IOptions<RabbitMqOptions> options) : IConsumerFactory
    {
        protected readonly IServiceProvider serviceProvider = serviceProvider;

        public async Task<RabbitMqConsumer> BuildAsync(string consumerName, CancellationToken cancellationToken)
        {
            if (!options.Value.Consumers.TryGetValue(consumerName, out RabbitMqConsumerOptions? consumerOptions))
                throw new KeyNotFoundException($"No Consumer config named \"{consumerName}\"");

            ILoggerFactory loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
            ILogger<RabbitMqConsumer> logger = loggerFactory.CreateLogger<RabbitMqConsumer>();
            IRabbitMqChannelFactory rabbitMqChannelFactory = serviceProvider.GetRequiredService<IRabbitMqChannelFactory>();

            return new RabbitMqConsumer(logger, serviceProvider, rabbitMqChannelFactory, consumerOptions);
        }
    }
}
