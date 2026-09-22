namespace Sanyappc.Extensions.RabbitMq.Tests;

internal sealed class TemporaryQueue : IAsyncDisposable
{
    public string Name { get; } = $"test-{Guid.NewGuid():N}";

    public ValueTask DisposeAsync() => new(Broker.DeleteQueueAsync(Name));
}
