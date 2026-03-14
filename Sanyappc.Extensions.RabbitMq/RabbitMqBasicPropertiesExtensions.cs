using System.Diagnostics;

using RabbitMQ.Client;

namespace Sanyappc.Extensions.RabbitMq
{
    internal static class RabbitMqBasicPropertiesExtensions
    {

        private static readonly ActivitySource ActivitySource = RabbitMqTelemetry.ActivitySource;

        private static readonly DistributedContextPropagator distributedContextPropagator = DistributedContextPropagator.CreateDefaultPropagator();

        public static Activity? StartReceiveActivity(this IReadOnlyBasicProperties properties, string queue)
        {
            distributedContextPropagator.ExtractTraceIdAndState(properties, getter, out string? traceId, out string? traceState);

            ActivityContext.TryParse(traceId, traceState, isRemote: true, out ActivityContext parentContext);

            Activity? activity = ActivitySource.StartActivity($"{queue} receive", ActivityKind.Consumer, parentContext);

            if (activity is null)
                return null;

            if (traceState is not null)
                activity.TraceStateString = traceState;

            activity.SetTag("messaging.system", "rabbitmq");
            activity.SetTag("messaging.destination.name", queue);
            activity.SetTag("messaging.operation", "receive");

            if (properties.CorrelationId is not null)
                activity.SetTag("messaging.message.conversation_id", properties.CorrelationId);

            if (properties.MessageId is not null)
                activity.SetTag("messaging.message.id", properties.MessageId);

            return activity;

            static void getter(object? carrier, string name, out string? value, out IEnumerable<string>? values)
            {
                values = null;

                IReadOnlyBasicProperties basicProperties = carrier as IReadOnlyBasicProperties ?? throw new InvalidOperationException();
                if (basicProperties.Headers is not null && basicProperties.Headers.TryGetValue(name, out object? objectValue))
                    value = $"{objectValue}";
                else
                    value = null;
            }
        }

        public static Activity? StartPublishActivity(string queue)
        {
            return ActivitySource.StartActivity($"{queue} publish", ActivityKind.Producer)?.WithMessagingTags(queue, "publish");
        }

        public static Activity? StartRequestActivity(string queue)
        {
            return ActivitySource.StartActivity($"{queue} request", ActivityKind.Client)?.WithMessagingTags(queue, "request");
        }

        private static Activity WithMessagingTags(this Activity activity, string queue, string operation)
        {
            activity.SetTag("messaging.system", "rabbitmq");
            activity.SetTag("messaging.destination.name", queue);
            activity.SetTag("messaging.operation", operation);
            return activity;
        }

        public static IBasicProperties Inject(this IBasicProperties properties, Activity? activity)
        {
            distributedContextPropagator.Inject(activity, properties, setter);

            return properties;

            static void setter(object? carrier, string name, string value)
            {
                IBasicProperties basicProperties = carrier as IBasicProperties ?? throw new InvalidOperationException();

                basicProperties.Headers ??= new Dictionary<string, object?>();
                basicProperties.Headers.TryAdd(name, value);
            }
        }
    }
}
