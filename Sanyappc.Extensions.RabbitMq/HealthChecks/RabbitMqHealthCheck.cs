using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Sanyappc.Extensions.RabbitMq;

internal class RabbitMqHealthCheck(IRabbitMqChannelFactory channelFactory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await channelFactory.CheckAsync(cancellationToken)
                .ConfigureAwait(false);

            return HealthCheckResult.Healthy();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, ex.Message, ex);
        }
    }
}
