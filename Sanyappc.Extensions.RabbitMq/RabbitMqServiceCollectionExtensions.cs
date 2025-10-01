using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Sanyappc.Extensions.RabbitMq
{
    public static class RabbitMqServiceCollectionExtensions
    {
        public static IServiceCollection AddNamedRabbitMqService(this IServiceCollection services, IConfiguration configuration, string sectionName)
        {
            services.AddLogging();

            services.AddOptions<RabbitMqOptions>()
                .Bind(configuration.GetSection(sectionName))
                .ValidateDataAnnotations()
                .ValidateOnStart();

            services.TryAddSingleton<IRabbitMqChannelFactory, RabbitMqChannelFactory>();
            services.TryAddTransient<IRabbitMqPublishService, RabbitMqPublishService>();
            services.AddSingleton<IConsumerFactory, ConsumerFactory>();

            return services;
        }
    }
}
