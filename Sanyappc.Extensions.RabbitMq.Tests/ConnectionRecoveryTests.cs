namespace Sanyappc.Extensions.RabbitMq.Tests;

public sealed class ConnectionRecoveryTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task AConsumerKeepsConsumingAfterItsConnectionDrops()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await Broker.SkipUnlessRunningAsync(cancellationToken);
        await using ConsumerUnderTest consumer = await ConsumerUnderTest.StartAsync<InboxProcessor>(Patience, cancellationToken);

        await Broker.PublishAsync(consumer.Queue, "before", cancellationToken);
        await consumer.Inbox.WaitForAsync("before", Patience, cancellationToken);

        consumer.Proxy.Sever();
        await Broker.PublishAsync(consumer.Queue, "after", cancellationToken);

        await consumer.Inbox.WaitForAsync("after", Patience, cancellationToken);
        Assert.False(consumer.Consuming.IsCompleted);
    }

    [Fact]
    public async Task EveryRecoveryIsMeasured()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await Broker.SkipUnlessRunningAsync(cancellationToken);
        await using ConsumerUnderTest consumer = await ConsumerUnderTest.StartAsync<InboxProcessor>(Patience, cancellationToken);
        using Measurements recoveries = new("sanyappc.rabbitmq.connection.recovery.duration", "server.port", consumer.Proxy.Port);

        await Broker.PublishAsync(consumer.Queue, "before", cancellationToken);
        await consumer.Inbox.WaitForAsync("before", Patience, cancellationToken);
        consumer.Proxy.Sever();

        Measurement recovery = await recoveries.NextAsync(Patience, cancellationToken);

        // At least the one-second interval the client waits before its first reconnect attempt.
        Assert.InRange(recovery.Value, 0.9, Patience.TotalSeconds);
    }

    [Fact]
    public async Task AConsumerGivesUpWhenItsConnectionStaysDown()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await Broker.SkipUnlessRunningAsync(cancellationToken);
        await using ConsumerUnderTest consumer = await ConsumerUnderTest.StartAsync<InboxProcessor>(TimeSpan.FromSeconds(3), cancellationToken);

        await Broker.PublishAsync(consumer.Queue, "before", cancellationToken);
        await consumer.Inbox.WaitForAsync("before", Patience, cancellationToken);

        consumer.Proxy.Refuse();
        consumer.Proxy.Sever();

        RabbitMqUnavailableException failure = await Assert.ThrowsAsync<RabbitMqUnavailableException>(
            () => consumer.Consuming.WaitAsync(Patience, cancellationToken));

        Assert.Contains("was not recovered within", failure.Message);
    }

    [Fact]
    public async Task AChannelTheBrokerClosesAloneFailsAtOnce()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await Broker.SkipUnlessRunningAsync(cancellationToken);
        await using ConsumerUnderTest consumer = await ConsumerUnderTest.StartAsync<DoubleAckingProcessor>(TimeSpan.FromMinutes(1), cancellationToken);

        await Broker.PublishAsync(consumer.Queue, "acked twice", cancellationToken);

        // Far inside the recovery timeout: waiting for a recovery that never comes would take the whole minute.
        RabbitMqUnavailableException failure = await Assert.ThrowsAsync<RabbitMqUnavailableException>(
            () => consumer.Consuming.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken));

        Assert.Contains("shut down unexpectedly", failure.Message);
    }
}
