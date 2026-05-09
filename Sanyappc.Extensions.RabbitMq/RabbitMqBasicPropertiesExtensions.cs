using System.Diagnostics;
using System.Text;

using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Sanyappc.Extensions.RabbitMq;

internal static class RabbitMqBasicPropertiesExtensions
{
    private static readonly ActivitySource ActivitySource = RabbitMqTelemetry.ActivitySource;

    private static readonly DistributedContextPropagator distributedContextPropagator = DistributedContextPropagator.CreateDefaultPropagator();

    public static Activity? StartProcessActivity(this BasicDeliverEventArgs @event, string queue, string serverAddress, int serverPort)
    {
        IReadOnlyBasicProperties properties = @event.BasicProperties;

        distributedContextPropagator.ExtractTraceIdAndState(properties, getter, out string? traceId, out string? traceState);
        ActivityContext.TryParse(traceId, traceState, isRemote: true, out ActivityContext parentContext);

        TagList tags = RabbitMqTelemetry.BuildTags(queue, RabbitMqTelemetry.ProcessOperation, serverAddress, serverPort);
        tags.Add(RabbitMqTelemetry.DeliveryTagTag, @event.DeliveryTag);
        tags.Add(RabbitMqTelemetry.MessageBodySizeTag, @event.Body.Length);

        if (properties.MessageId is not null)
            tags.Add(RabbitMqTelemetry.MessageIdTag, properties.MessageId);

        if (properties.CorrelationId is not null)
            tags.Add(RabbitMqTelemetry.ConversationIdTag, properties.CorrelationId);

        Activity? activity = ActivitySource.StartActivity(
            $"{RabbitMqTelemetry.ProcessOperation} {queue}",
            ActivityKind.Consumer,
            parentContext,
            tags);

        if (activity is not null && traceState is not null)
            activity.TraceStateString = traceState;

        return activity;

        static void getter(object? carrier, string name, out string? value, out IEnumerable<string>? values)
        {
            values = null;

            IReadOnlyBasicProperties basicProperties = carrier as IReadOnlyBasicProperties ?? throw new InvalidOperationException();
            if (basicProperties.Headers is not null && basicProperties.Headers.TryGetValue(name, out object? objectValue))
                value = objectValue is byte[] bytes ? Encoding.UTF8.GetString(bytes) : objectValue?.ToString();
            else
                value = null;
        }
    }

    public static Activity? StartPublishActivity(string queue, string serverAddress, int serverPort, int bodySize)
    {
        TagList tags = RabbitMqTelemetry.BuildTags(queue, RabbitMqTelemetry.SendOperation, serverAddress, serverPort);
        tags.Add(RabbitMqTelemetry.MessageBodySizeTag, bodySize);

        return ActivitySource.StartActivity(
            $"{RabbitMqTelemetry.SendOperation} {queue}",
            ActivityKind.Producer,
            default(ActivityContext),
            tags);
    }

    public static Activity? StartRequestActivity(string queue, string serverAddress, int serverPort, int bodySize)
    {
        TagList tags = RabbitMqTelemetry.BuildTags(queue, RabbitMqTelemetry.SendOperation, serverAddress, serverPort);
        tags.Add(RabbitMqTelemetry.MessageBodySizeTag, bodySize);

        return ActivitySource.StartActivity(
            $"{RabbitMqTelemetry.SendOperation} {queue}",
            ActivityKind.Client,
            default(ActivityContext),
            tags);
    }

    public static void SetError(this Activity? activity, Exception exception, string errorType)
    {
        if (activity is null)
            return;

        activity.SetStatus(ActivityStatusCode.Error, exception.Message);
        activity.SetTag(RabbitMqTelemetry.ErrorTypeTag, errorType);
        activity.AddException(exception);
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
