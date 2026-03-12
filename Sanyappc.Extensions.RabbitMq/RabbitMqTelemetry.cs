using System.Diagnostics;

namespace Sanyappc.Extensions.RabbitMq
{
    public static class RabbitMqTelemetry
    {
        public const string ActivitySourceName = "Sanyappc.Extensions.RabbitMq";

        internal static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    }
}
