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

        public async Task<RabbitMqConsumer> BuildAsync(CancellationToken cancellationToken, string connectionName = null)
        {
            RabbitMqConnectionSettings? rabbitOptions;

            Console.WriteLine(connectionName);

            if (connectionName == null)
                rabbitOptions = options.Value.Connection;
            else if (!options.Value.Connections.TryGetValue(connectionName, out rabbitOptions))
                throw new KeyNotFoundException($"No Connection config named \"{connectionName}\"");

            ILoggerFactory loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
            ILogger<RabbitMqConsumer> logger = loggerFactory.CreateLogger<RabbitMqConsumer>();
            IRabbitMqChannelFactory rabbitMqChannelFactory = serviceProvider.GetRequiredService<IRabbitMqChannelFactory>();

            return new RabbitMqConsumer(logger, serviceProvider, rabbitMqChannelFactory, connectionName, rabbitOptions.QueueName);
        }
    }
}
