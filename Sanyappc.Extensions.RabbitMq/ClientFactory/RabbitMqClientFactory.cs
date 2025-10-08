using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Sanyappc.Extensions.RabbitMq.ConsumeFactory;

namespace Sanyappc.Extensions.RabbitMq
{
    internal class RabbitMqClientFactory(
        IServiceProvider serviceProvider,
        ILoggerFactory loggerFactory,
        IRabbitMqChannelFactory rabbitMqChannelFactory,
        IOptions<RabbitMqOptions> options) : IRabbitMqClientFactory
    {
        protected readonly IServiceProvider serviceProvider = serviceProvider;
        protected readonly ILoggerFactory loggerFactory = loggerFactory;
        protected readonly IRabbitMqChannelFactory rabbitMqChannelFactory = rabbitMqChannelFactory;

        public IRabbitMqConsumer BuildConsumer(string connectionName = null)
        {
            RabbitMqConnectionSettings? rabbitOptions;

            if (connectionName == null)
                rabbitOptions = options.Value.Connection;
            else if (!options.Value.Connections.TryGetValue(connectionName, out rabbitOptions))
                throw new KeyNotFoundException($"No Connection config named \"{connectionName}\"");

            return new RabbitMqConsumer(
                loggerFactory.CreateLogger<RabbitMqConsumer>(),
                serviceProvider,
                rabbitMqChannelFactory,
                connectionName!,
                rabbitOptions!.QueueName);
        }

        public IRabbitMqPublisher BuildPublisher(string connectionName = null)
        {
            RabbitMqConnectionSettings? rabbitOptions;

            if (connectionName == null)
                rabbitOptions = options.Value.Connection;
            else if (!options.Value.Connections.TryGetValue(connectionName, out rabbitOptions))
                throw new KeyNotFoundException($"No Connection config named \"{connectionName}\"");

            return new RabbitMqPublisher(
                loggerFactory.CreateLogger<RabbitMqPublisher>(),
                rabbitMqChannelFactory,
                connectionName!,
                rabbitOptions!.QueueName);
        }
    }
}
