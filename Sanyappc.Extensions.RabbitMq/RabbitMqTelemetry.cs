using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Sanyappc.Extensions.RabbitMq
{
    public static class RabbitMqTelemetry
    {
        public const string ActivitySourceName = "Sanyappc.Extensions.RabbitMq";
        public const string MeterName = "Sanyappc.Extensions.RabbitMq";

        internal static readonly ActivitySource ActivitySource = new(ActivitySourceName);
        internal static readonly Meter Meter = new(MeterName);

        internal static readonly Counter<long> PublishedMessages =
            Meter.CreateCounter<long>("messaging.publish.messages", "{message}", "Number of messages published.");

        internal static readonly Counter<long> ReceivedMessages =
            Meter.CreateCounter<long>("messaging.receive.messages", "{message}", "Number of messages received.");

        internal static readonly Histogram<double> ProcessDuration =
            Meter.CreateHistogram<double>("messaging.process.duration", "s", "Duration of message processing.");
    }
}
