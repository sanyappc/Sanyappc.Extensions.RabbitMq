using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Sanyappc.Extensions.RabbitMq.Tests;

// Nobody consumes the queue, so every request waits for a reply that never comes.
public sealed class ReplyTimeoutTests
{
    private const string OperationDuration = "messaging.client.operation.duration";
    private const string DestinationTag = "messaging.destination.name";
    private const string ErrorTypeTag = "error.type";

    private static readonly TimeSpan ShortReplyTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private static ServiceProvider PublisherWith(TimeSpan replyTimeout)
    {
        ServiceCollection services = new();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddRabbitMqService(options =>
        {
            options.Hostname = Broker.Hostname;
            options.Port = Broker.Port;
            options.Username = Broker.Username;
            options.Password = Broker.Password;
            options.ReplyTimeout = replyTimeout;
        });

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task AnUnansweredRequestTimesOut()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await Broker.SkipUnlessRunningAsync(cancellationToken);
        await using TemporaryQueue queue = new();
        await using ServiceProvider provider = PublisherWith(ShortReplyTimeout);
        IRabbitMqPublishService publisher = provider.GetRequiredService<IRabbitMqPublishService>();

        RabbitMqTimeoutException failure = await Assert.ThrowsAsync<RabbitMqTimeoutException>(
            () => publisher.RequestAsync<string, string>(queue.Name, "ping", cancellationToken: cancellationToken));

        Assert.Contains($"timed out after {ShortReplyTimeout}", failure.Message);
    }

    [Fact]
    public async Task AnUnansweredRequestWithoutAReplyBodyTimesOut()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await Broker.SkipUnlessRunningAsync(cancellationToken);
        await using TemporaryQueue queue = new();
        await using ServiceProvider provider = PublisherWith(ShortReplyTimeout);
        IRabbitMqPublishService publisher = provider.GetRequiredService<IRabbitMqPublishService>();

        await Assert.ThrowsAsync<RabbitMqTimeoutException>(
            () => publisher.RequestAsync(queue.Name, "ping", cancellationToken: cancellationToken));
    }

    [Fact]
    public async Task ATimeoutIsCountedAsATimeout()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await Broker.SkipUnlessRunningAsync(cancellationToken);
        await using TemporaryQueue queue = new();
        await using ServiceProvider provider = PublisherWith(ShortReplyTimeout);
        IRabbitMqPublishService publisher = provider.GetRequiredService<IRabbitMqPublishService>();
        using Measurements requests = new(OperationDuration, DestinationTag, queue.Name);

        await Assert.ThrowsAnyAsync<RabbitMqException>(
            () => publisher.RequestAsync<string, string>(queue.Name, "ping", cancellationToken: cancellationToken));

        Measurement request = await requests.NextAsync(Patience, cancellationToken);
        Assert.Equal("timeout", request.Tags[ErrorTypeTag]);
    }

    [Fact]
    public async Task ACallerThatGivesUpFirstGetsItsOwnCancellation()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await Broker.SkipUnlessRunningAsync(cancellationToken);
        await using TemporaryQueue queue = new();
        await using ServiceProvider provider = PublisherWith(Patience);
        IRabbitMqPublishService publisher = provider.GetRequiredService<IRabbitMqPublishService>();
        using CancellationTokenSource caller = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        caller.CancelAfter(TimeSpan.FromMilliseconds(500));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => publisher.RequestAsync<string, string>(queue.Name, "ping", cancellationToken: caller.Token));
    }
}
