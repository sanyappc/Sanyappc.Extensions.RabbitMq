namespace Sanyappc.Extensions.RabbitMq.Tests;

internal sealed record Measurement(double Value, IReadOnlyDictionary<string, object?> Tags);
