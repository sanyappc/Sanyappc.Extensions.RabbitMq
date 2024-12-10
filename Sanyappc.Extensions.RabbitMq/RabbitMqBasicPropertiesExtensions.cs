using System.Diagnostics;

using RabbitMQ.Client;

namespace Sanyappc.Extensions.RabbitMq
{
    internal static class RabbitMqBasicPropertiesExtensions
    {
        private const string MessageDeliveryActivityName = "Sanyappc.Extensions.RabbitMq.RabbitMqMessageDelivery";
        private const string CorrelationIdTagKey = "correlation-id";

        private static readonly DistributedContextPropagator distributedContextPropagator = DistributedContextPropagator.CreateDefaultPropagator();

        public static Activity StartActivity(this IReadOnlyBasicProperties properties)
        {
            Activity activity = new(MessageDeliveryActivityName);

            distributedContextPropagator.ExtractTraceIdAndState(properties, getter, out string? traceId, out string? traceState);

            if (traceId is not null)
                activity.SetParentId(traceId);

            if (traceState is not null)
                activity.TraceStateString = traceState;

            if (properties.CorrelationId is not null)
                activity.SetTag(CorrelationIdTagKey, properties.CorrelationId);

            return activity.Start();

            static void getter(object? carrier, string name, out string? value, out IEnumerable<string>? values)
            {
                values = null;

                IBasicProperties basicProperties = carrier as IBasicProperties ?? throw new InvalidOperationException();
                if (basicProperties.Headers is not null && basicProperties.Headers.TryGetValue(name, out object? objectValue))
                    value = $"{objectValue}";
                else
                    value = null;
            }
        }

        public static IBasicProperties Inject(this IBasicProperties properties, Activity? activity)
        {
            distributedContextPropagator.Inject(activity, properties, setter);

            return properties;

            static void setter(object? carrier, string name, string value)
            {
                if (name == CorrelationIdTagKey)
                    return;

                IBasicProperties basicProperties = carrier as IBasicProperties ?? throw new InvalidOperationException();

                basicProperties.Headers ??= new Dictionary<string, object?>();
                basicProperties.Headers.TryAdd(name, value);
            }
        }
    }
}
