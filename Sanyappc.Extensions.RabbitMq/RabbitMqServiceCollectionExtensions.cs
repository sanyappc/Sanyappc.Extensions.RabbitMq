using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Sanyappc.Extensions.RabbitMq
{
    public static class RabbitMqServiceCollectionExtensions
    {
        public static IServiceCollection AddRabbitMqService(this IServiceCollection services, Action<RabbitMqOptions>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(services);

            services.AddLogging();

            OptionsBuilder<RabbitMqOptions> optionsBuilder = services.AddOptions<RabbitMqOptions>()
                .ValidateDataAnnotations()
                .ValidateOnStart();

            if (configure is not null)
                optionsBuilder.Configure(configure);

            optionsBuilder.BindConfiguration("RabbitMq");

            services.TryAddSingleton<IRabbitMqChannelFactory, RabbitMqChannelFactory>();

            services.TryAddTransient<IRabbitMqConsumeService, RabbitMqConsumeService>();
            services.TryAddTransient<IRabbitMqPublishService, RabbitMqPublishService>();

            return services;
        }

        public static IServiceCollection AddRabbitMqConsumer<T>(this IServiceCollection services, string queue)
            where T : class, IRabbitMqMessageProcessingService
        {
            ArgumentNullException.ThrowIfNull(services);

            services.AddRabbitMqService();
            services.TryAddScoped<T>();
            services.AddHostedService(sp =>
                new RabbitMqConsumerHostedService<T>(sp.GetRequiredService<IRabbitMqConsumeService>(), queue));

            return services;
        }
    }
}
