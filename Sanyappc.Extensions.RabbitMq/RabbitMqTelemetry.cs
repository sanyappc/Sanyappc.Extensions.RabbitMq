using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;

namespace Sanyappc.Extensions.RabbitMq;

public static class RabbitMqTelemetry
{
    private static readonly string? version = typeof(RabbitMqTelemetry).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
        ?.InformationalVersion;

    private static readonly InstrumentAdvice<double> durationAdvice = new()
    {
        HistogramBucketBoundaries = [0.005, 0.01, 0.025, 0.05, 0.075, 0.1, 0.25, 0.5, 0.75, 1, 2.5, 5, 7.5, 10]
    };

    private static readonly InstrumentAdvice<double> recoveryDurationAdvice = new()
    {
        HistogramBucketBoundaries = [1, 2.5, 5, 7.5, 10, 15, 30, 60, 120, 300]
    };

    internal const string SystemValue = "rabbitmq";

    internal const string SystemTag = "messaging.system";
    internal const string DestinationNameTag = "messaging.destination.name";
    internal const string OperationNameTag = "messaging.operation.name";
    internal const string OperationTypeTag = "messaging.operation.type";
    internal const string MessageIdTag = "messaging.message.id";
    internal const string ConversationIdTag = "messaging.message.conversation_id";
    internal const string MessageBodySizeTag = "messaging.message.body.size";
    internal const string RoutingKeyTag = "messaging.rabbitmq.destination.routing_key";
    internal const string DeliveryTagTag = "messaging.rabbitmq.message.delivery_tag";
    internal const string ServerAddressTag = "server.address";
    internal const string ServerPortTag = "server.port";
    internal const string ErrorTypeTag = "error.type";

    internal const string SendOperation = "send";
    internal const string ReceiveOperation = "receive";
    internal const string ProcessOperation = "process";

    internal const string TimeoutError = "timeout";
    internal const string RequestRejectedError = "request_rejected";
    internal const string BrokerUnavailableError = "broker_unavailable";

    internal static readonly ActivitySource ActivitySource = new(ActivitySourceName, version);
    internal static readonly Meter Meter = new(MeterName, version);

    internal static readonly Counter<long> SentMessages =
        Meter.CreateCounter<long>("messaging.client.sent.messages", "{message}", "Number of messages producer attempted to send to the broker.");

    internal static readonly Counter<long> ConsumedMessages =
        Meter.CreateCounter<long>("messaging.client.consumed.messages", "{message}", "Number of messages that were delivered to the application.");

    internal static readonly Histogram<double> ProcessDuration =
        Meter.CreateHistogram<double>("messaging.process.duration", "s", "Duration of message processing.", advice: durationAdvice);

    internal static readonly Histogram<double> OperationDuration =
        Meter.CreateHistogram<double>("messaging.client.operation.duration", "s", "Duration of messaging operation initiated by a producer or consumer client.", advice: durationAdvice);

    internal static readonly Histogram<double> ConnectionRecoveryDuration =
        Meter.CreateHistogram<double>("sanyappc.rabbitmq.connection.recovery.duration", "s", "Time from losing the broker connection to having it, its channels and their consumers back.", advice: recoveryDurationAdvice);

    public const string ActivitySourceName = "Sanyappc.Extensions.RabbitMq";
    public const string MeterName = "Sanyappc.Extensions.RabbitMq";

    internal static TagList BuildTags(string queue, string operation, string serverAddress, int serverPort, string? errorType = null)
    {
        TagList tags = new()
        {
            { SystemTag, SystemValue },
            { DestinationNameTag, queue },
            { OperationNameTag, operation },
            { OperationTypeTag, operation },
            { RoutingKeyTag, queue },
            { ServerAddressTag, serverAddress },
            { ServerPortTag, serverPort }
        };

        if (errorType is not null)
            tags.Add(ErrorTypeTag, errorType);

        return tags;
    }

    internal static void IncrementSent(string queue, string serverAddress, int serverPort, string? errorType)
    {
        TagList tags = BuildTags(queue, SendOperation, serverAddress, serverPort, errorType);
        SentMessages.Add(1, tags);
    }

    internal static void IncrementConsumed(string queue, string serverAddress, int serverPort)
    {
        TagList tags = BuildTags(queue, ReceiveOperation, serverAddress, serverPort);
        ConsumedMessages.Add(1, tags);
    }

    internal static void RecordSendDuration(string queue, string serverAddress, int serverPort, long startTimestamp, string? errorType)
    {
        TagList tags = BuildTags(queue, SendOperation, serverAddress, serverPort, errorType);
        OperationDuration.Record(Stopwatch.GetElapsedTime(startTimestamp).TotalSeconds, tags);
    }

    internal static void RecordProcessDuration(string queue, string serverAddress, int serverPort, long startTimestamp, string? errorType)
    {
        TagList tags = BuildTags(queue, ProcessOperation, serverAddress, serverPort, errorType);
        ProcessDuration.Record(Stopwatch.GetElapsedTime(startTimestamp).TotalSeconds, tags);
    }

    internal static void RecordConnectionRecovery(string serverAddress, int serverPort, TimeSpan outage)
    {
        TagList tags = new()
        {
            { SystemTag, SystemValue },
            { ServerAddressTag, serverAddress },
            { ServerPortTag, serverPort }
        };

        ConnectionRecoveryDuration.Record(outage.TotalSeconds, tags);
    }

    internal static string GetErrorType(Exception ex) => ex switch
    {
        RabbitMqTimeoutException => TimeoutError,
        RabbitMqRequestRejectedException => RequestRejectedError,
        RabbitMqUnavailableException => BrokerUnavailableError,
        _ => ex.GetType().FullName ?? ex.GetType().Name
    };
}
