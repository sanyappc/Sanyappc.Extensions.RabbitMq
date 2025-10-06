using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Sanyappc.Extensions.RabbitMq;

namespace Amadesci.Extensions.NamedRabbitMq.PublishFactory
{
    internal class PublisherFactory(
        IServiceProvider serviceProvider,
        IOptions<RabbitMqOptions> options) : IPublisherFactory
    {
        public async Task<IRabbitMqPublisher> BuildAsync(CancellationToken cancellationToken, string connectionName = null)
        {
            RabbitMqConnectionSettings? rabbitOptions;

            if (connectionName == null)
                rabbitOptions = options.Value.Connection;
            if (!options.Value.Connections.TryGetValue(connectionName, out RabbitMqConnectionSettings? connectionOptions))
                throw new KeyNotFoundException($"No Connection config named \"{connectionName}\"");

            ILoggerFactory loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
            ILogger<RabbitMqPublisher> logger = loggerFactory.CreateLogger<RabbitMqPublisher>();
            IRabbitMqChannelFactory rabbitMqChannelFactory = serviceProvider.GetRequiredService<IRabbitMqChannelFactory>();

            return new RabbitMqPublisher(logger, rabbitMqChannelFactory, connectionName, connectionOptions.QueueName);
        }
    }
}
