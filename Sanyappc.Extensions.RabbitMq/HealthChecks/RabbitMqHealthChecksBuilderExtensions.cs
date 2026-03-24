using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Sanyappc.Extensions.RabbitMq;

public static class RabbitMqHealthChecksBuilderExtensions
{
    public static IHealthChecksBuilder AddRabbitMq(this IHealthChecksBuilder builder, string name = "rabbitmq")
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Add(new HealthCheckRegistration(
            name,
            sp => new RabbitMqHealthCheck(sp.GetRequiredService<IRabbitMqChannelFactory>()),
            failureStatus: null,
            tags: null));

        return builder;
    }

    public static IHealthChecksBuilder AddRabbitMq(this IHealthChecksBuilder builder, string connectionName, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(connectionName);

        builder.Add(new HealthCheckRegistration(
            name ?? $"rabbitmq:{connectionName}",
            sp => new RabbitMqHealthCheck(sp.GetRequiredKeyedService<IRabbitMqChannelFactory>(connectionName)),
            failureStatus: null,
            tags: null));

        return builder;
    }
}
