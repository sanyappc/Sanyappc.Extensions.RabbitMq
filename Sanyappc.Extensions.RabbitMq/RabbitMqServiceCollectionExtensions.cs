using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
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

            optionsBuilder.BindConfiguration("RabbitMq");

            if (configure is not null)
                optionsBuilder.Configure(configure);

            services.TryAddSingleton<IRabbitMqChannelFactory>(sp =>
                new RabbitMqChannelFactory(
                    sp.GetRequiredService<ILogger<RabbitMqChannelFactory>>(),
                    sp.GetRequiredService<IOptions<RabbitMqOptions>>().Value));

            services.TryAddTransient<IRabbitMqConsumeService, RabbitMqConsumeService>();
            services.TryAddTransient<IRabbitMqPublishService, RabbitMqPublishService>();

            return services;
        }

        public static IServiceCollection AddRabbitMqService(this IServiceCollection services, string name, Action<RabbitMqOptions>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(name);

            services.AddLogging();

            OptionsBuilder<RabbitMqOptions> optionsBuilder = services.AddOptions<RabbitMqOptions>(name)
                .ValidateDataAnnotations()
                .ValidateOnStart();

            optionsBuilder.BindConfiguration($"RabbitMq:{name}");

            if (configure is not null)
                optionsBuilder.Configure(configure);

            services.TryAddKeyedSingleton<IRabbitMqChannelFactory>(name, (sp, _) =>
                new RabbitMqChannelFactory(
                    sp.GetRequiredService<ILogger<RabbitMqChannelFactory>>(),
                    sp.GetRequiredService<IOptionsMonitor<RabbitMqOptions>>().Get(name)));

            services.TryAddKeyedTransient<IRabbitMqPublishService>(name, (sp, _) =>
                new RabbitMqPublishService(
                    sp.GetRequiredService<ILogger<RabbitMqPublishService>>(),
                    sp.GetRequiredKeyedService<IRabbitMqChannelFactory>(name)));

            services.TryAddKeyedTransient<IRabbitMqConsumeService>(name, (sp, _) =>
                new RabbitMqConsumeService(
                    sp.GetRequiredService<ILogger<RabbitMqConsumeService>>(),
                    sp.GetRequiredKeyedService<IRabbitMqChannelFactory>(name),
                    sp.GetRequiredService<IServiceScopeFactory>()));

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

        public static IServiceCollection AddRabbitMqConsumer<T>(this IServiceCollection services, string name, string queue)
            where T : class, IRabbitMqMessageProcessingService
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(name);

            services.AddRabbitMqService(name);
            services.TryAddScoped<T>();
            services.AddHostedService(sp =>
                new RabbitMqConsumerHostedService<T>(sp.GetRequiredKeyedService<IRabbitMqConsumeService>(name), queue));

            return services;
        }
    }
}
