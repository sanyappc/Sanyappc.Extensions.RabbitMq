using System.Globalization;
using System.Net.Sockets;
using System.Text.Json;

using RabbitMQ.Client;

namespace Sanyappc.Extensions.RabbitMq.Tests;

// A real broker: recovery lives in RabbitMQ.Client's reaction to a dropped socket, which no fake reproduces.
internal static class Broker
{
    private static bool Required { get; } = RequiredFromEnvironment();

    public static string Hostname { get; } = Environment.GetEnvironmentVariable("RABBITMQ_TEST_HOSTNAME") ?? "localhost";

    public static int Port { get; } = PortFromEnvironment();

    public static string Username { get; } = Environment.GetEnvironmentVariable("RABBITMQ_TEST_USERNAME") ?? "guest";

    public static string Password { get; } = Environment.GetEnvironmentVariable("RABBITMQ_TEST_PASSWORD") ?? "guest";

    private static int PortFromEnvironment()
    {
        string? configured = Environment.GetEnvironmentVariable("RABBITMQ_TEST_PORT");
        if (configured is null)
            return 5672;

        return int.Parse(configured, CultureInfo.InvariantCulture);
    }

    private static bool RequiredFromEnvironment()
    {
        string? configured = Environment.GetEnvironmentVariable("RABBITMQ_TEST_REQUIRED");
        if (configured is null)
            return false;

        return bool.Parse(configured);
    }

    private static async Task<IConnection> ConnectAsync(CancellationToken cancellationToken)
    {
        ConnectionFactory factory = new()
        {
            HostName = Hostname,
            Port = Port,
            UserName = Username,
            Password = Password
        };

        return await factory.CreateConnectionAsync(cancellationToken);
    }

    public static async Task SkipUnlessRunningAsync(CancellationToken cancellationToken)
    {
        using TcpClient probe = new();
        try
        {
            await probe.ConnectAsync(Hostname, Port, cancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
        }
        catch (Exception exception) when (exception is SocketException or TimeoutException)
        {
            // CI sets it: a broker service that failed to start must fail the run, not turn every broker test into a skip.
            if (Required)
                Assert.Fail($"No RabbitMQ broker at {Hostname}:{Port}, and RABBITMQ_TEST_REQUIRED is set.");

            Assert.Skip($"No RabbitMQ broker at {Hostname}:{Port}. Start one with: docker run -d --rm -p 5672:5672 rabbitmq:4");
        }
    }

    public static async Task DeclareQueueAsync(string queue, CancellationToken cancellationToken)
    {
        await using IConnection connection = await ConnectAsync(cancellationToken);
        await using IChannel channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
        await channel.QueueDeclareAsync(queue, true, false, false, cancellationToken: cancellationToken);
    }

    public static async Task PublishAsync(string queue, string text, CancellationToken cancellationToken)
    {
        await using IConnection connection = await ConnectAsync(cancellationToken);
        await using IChannel channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
        await channel.BasicPublishAsync(string.Empty, queue, JsonSerializer.SerializeToUtf8Bytes(text), cancellationToken);
    }

    public static async Task DeleteQueueAsync(string queue)
    {
        await using IConnection connection = await ConnectAsync(CancellationToken.None);
        await using IChannel channel = await connection.CreateChannelAsync();
        await channel.QueueDeleteAsync(queue);
    }
}
