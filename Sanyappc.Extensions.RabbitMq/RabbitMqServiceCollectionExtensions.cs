using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Sanyappc.Extensions.RabbitMq
{
    public static class RabbitMqServiceCollectionExtensions
    {
        public static IServiceCollection AddRabbitMqService(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);

            services.AddLogging();
            services.AddOptions<RabbitMqOptions>()
                .BindConfiguration(string.Empty)
                .ValidateDataAnnotations()
                .ValidateOnStart();

            services.TryAddSingleton<IRabbitMqChannelFactory, RabbitMqChannelFactory>();

            services.TryAddTransient<IRabbitMqConsumeService, RabbitMqConsumeService>();
            services.TryAddTransient<IRabbitMqPublishService, RabbitMqPublishService>();

            return services;
        }
    }
}
