using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;

namespace Sanyappc.Extensions.RabbitMq;

public static class RabbitMqTelemetry
{
    public const string ActivitySourceName = "Sanyappc.Extensions.RabbitMq";
    public const string MeterName = "Sanyappc.Extensions.RabbitMq";

    private static readonly string? version = typeof(RabbitMqTelemetry).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
        ?.InformationalVersion;

    internal static readonly ActivitySource ActivitySource = new(ActivitySourceName, version);
    internal static readonly Meter Meter = new(MeterName, version);

    internal static readonly Counter<long> PublishedMessages =
        Meter.CreateCounter<long>("messaging.publish.messages", "{message}", "Number of messages published.");

    internal static readonly Counter<long> ReceivedMessages =
        Meter.CreateCounter<long>("messaging.receive.messages", "{message}", "Number of messages received.");

    internal static readonly Histogram<double> ProcessDuration =
        Meter.CreateHistogram<double>("messaging.process.duration", "s", "Duration of message processing.");

    internal static readonly Counter<long> FailedMessages =
        Meter.CreateCounter<long>("messaging.client.operation.errors", "{message}", "Number of messaging operations that failed.");
}
