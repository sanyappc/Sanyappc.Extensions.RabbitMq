using System.Net;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Sanyappc.Extensions.RabbitMq.Tests;

// One consumer on its own queue, reaching the broker through a proxy the test can cut.
internal sealed class ConsumerUnderTest : IAsyncDisposable
{
    private const int RecoveryIntervalInSeconds = 1;

    private readonly ServiceProvider provider;
    private readonly CancellationTokenSource stopping;

    private ConsumerUnderTest(string queue, SeverableProxy proxy, ServiceProvider provider, CancellationTokenSource stopping, Task consuming)
    {
        Queue = queue;
        Proxy = proxy;
        this.provider = provider;
        this.stopping = stopping;
        Consuming = consuming;
    }

    public string Queue { get; }

    public SeverableProxy Proxy { get; }

    public Inbox Inbox => provider.GetRequiredService<Inbox>();

    public Task Consuming { get; }

    public static async Task<ConsumerUnderTest> StartAsync<TProcessor>(int recoveryTimeoutInSeconds, CancellationToken cancellationToken)
        where TProcessor : class, IRabbitMqMessageProcessingService
    {
        string queue = $"recovery-{Guid.NewGuid():N}";

        // Declared up front, so a message published before the consumer attaches waits instead of being dropped as unroutable.
        await Broker.DeclareQueueAsync(queue, cancellationToken);

        SeverableProxy proxy = new(Broker.Hostname, Broker.Port);

        ServiceCollection services = new();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton<Inbox>();
        services.AddScoped<TProcessor>();
        services.AddRabbitMqService(options =>
        {
            options.Hostname = IPAddress.Loopback.ToString();
            options.Port = proxy.Port;
            options.Username = Broker.Username;
            options.Password = Broker.Password;
            options.RecoveryIntervalInSeconds = RecoveryIntervalInSeconds;
            options.RecoveryTimeoutInSeconds = recoveryTimeoutInSeconds;
        });

        ServiceProvider provider = services.BuildServiceProvider();
        CancellationTokenSource stopping = new();
        Task consuming = provider.GetRequiredService<IRabbitMqConsumeService>().ConsumeAsync<TProcessor>(queue, stopping.Token);

        return new ConsumerUnderTest(queue, proxy, provider, stopping, consuming);
    }

    public async ValueTask DisposeAsync()
    {
        await stopping.CancelAsync();
        await Consuming.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        await provider.DisposeAsync();
        await Proxy.DisposeAsync();
        await Broker.DeleteQueueAsync(Queue);
        stopping.Dispose();
    }
}
