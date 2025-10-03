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
        public async Task<IRabbitMqPublisher> BuildAsync(string publisherName, CancellationToken cancellationToken = default)
        {
            if (!options.Value.Publishers.TryGetValue(publisherName, out RabbitMqPublisherOptions? publisherOptions))
                throw new KeyNotFoundException($"No Publisher config named \"{publisherName}\"");

            ILoggerFactory loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
            ILogger<RabbitMqPublisher> logger = loggerFactory.CreateLogger<RabbitMqPublisher>();
            IRabbitMqChannelFactory rabbitMqChannelFactory = serviceProvider.GetRequiredService<IRabbitMqChannelFactory>();

            return new RabbitMqPublisher(logger, rabbitMqChannelFactory, publisherOptions);
        }
    }
}
