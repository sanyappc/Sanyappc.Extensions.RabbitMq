using Microsoft.Extensions.Logging.Testing;

namespace Sanyappc.Extensions.RabbitMq.Tests;

public sealed class MessageLogScopeTests
{
    private const int MessageReceivedEvent = 40;

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task EveryLineLoggedWhileAMessageIsProcessedCarriesTheMessagesOwnAttributes()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await Broker.SkipUnlessRunningAsync(cancellationToken);
        await using ConsumerUnderTest consumer = await ConsumerUnderTest.StartAsync<InboxProcessor>(Patience, cancellationToken);

        await Broker.PublishAsync(consumer.Queue, "hello", cancellationToken);
        await consumer.Inbox.WaitForAsync("hello", Patience, cancellationToken);

        FakeLogRecord received = Assert.Single(consumer.Logs.GetSnapshot(), record => record.Id.Id == MessageReceivedEvent);
        IEnumerable<KeyValuePair<string, object?>> scope = Assert.Single(received.Scopes.OfType<IEnumerable<KeyValuePair<string, object?>>>());
        Dictionary<string, object?> attributes = scope.ToDictionary(pair => pair.Key, pair => pair.Value);

        Assert.Equal(consumer.Queue, attributes["messaging.destination.name"]);
        Assert.Contains("messaging.message.id", attributes.Keys);
        Assert.Equal(1UL, attributes["messaging.rabbitmq.message.delivery_tag"]);
        Assert.DoesNotContain("TraceId", attributes.Keys);
        Assert.Equal($"queue {consumer.Queue}, message {attributes["messaging.message.id"] ?? "-"}, delivery tag 1", scope.ToString());
    }
}
