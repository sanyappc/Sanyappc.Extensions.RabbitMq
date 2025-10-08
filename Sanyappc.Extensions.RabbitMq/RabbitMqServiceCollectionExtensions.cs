using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Sanyappc.Extensions.RabbitMq
{
    public static class RabbitMqServiceCollectionExtensions
    {
        public static IServiceCollection AddNamedRabbitMqService(this IServiceCollection services, IConfiguration configuration, string sectionName = "RABBITMQ")
        {
            services.AddLogging();

            services.AddOptions<RabbitMqOptions>()
                .Bind(configuration.GetSection(sectionName))
                .ValidateDataAnnotations()
                .ValidateOnStart();

            services.TryAddSingleton<IRabbitMqChannelFactory, RabbitMqChannelFactory>();
            services.TryAddSingleton<IRabbitMqClientFactory, RabbitMqClientFactory>();

            return services;
        }
    }
}
