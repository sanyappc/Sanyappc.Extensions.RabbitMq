using System.Diagnostics;

using Microsoft.Extensions.Logging;

using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Sanyappc.Extensions.RabbitMq;

internal partial class RabbitMqChannelFactory(ILogger<RabbitMqChannelFactory> logger, RabbitMqOptions options) : IRabbitMqChannelFactory, IAsyncDisposable
{
    private const int defaultAmqpPort = 5672;

    private readonly ILogger<RabbitMqChannelFactory> logger = logger;
    private readonly ConnectionFactory connectionFactory = new()
    {
        HostName = options.Hostname,
        Port = options.Port,
        UserName = options.Username,
        Password = options.Password,
        AutomaticRecoveryEnabled = true,
        TopologyRecoveryEnabled = true,
        NetworkRecoveryInterval = options.RecoveryInterval
    };

    private readonly SemaphoreSlim semaphoreSlim = new(1, 1);
    private IConnection? connection;
    private long connectionLostAt;

    public string ServerAddress => connectionFactory.HostName;

    public int ServerPort => connectionFactory.Port == -1 ? defaultAmqpPort : connectionFactory.Port;

    public bool IsConnectionOpen => connection?.IsOpen ?? false;

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "RabbitMQ connection established to {server.address}:{server.port}")]
    private static partial void LogConnectionEstablished(ILogger logger, [TagName("server.address")] string hostname, [TagName("server.port")] int port);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "RabbitMQ connection disposed")]
    private static partial void LogConnectionDisposed(ILogger logger);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "RabbitMQ connection to {server.address}:{server.port} lost: {sanyappc.rabbitmq.shutdown.reason}. Reconnecting every {sanyappc.rabbitmq.connection.recovery.interval}")]
    private static partial void LogConnectionLost(
        ILogger logger,
        [TagName("server.address")] string hostname,
        [TagName("server.port")] int port,
        [TagName("sanyappc.rabbitmq.shutdown.reason")] string reason,
        [TagName("sanyappc.rabbitmq.connection.recovery.interval")] TimeSpan recoveryInterval);

    // Information without the exception: it repeats every interval for as long as the outage lasts, and the loss was already the warning.
    [LoggerMessage(EventId = 4, Level = LogLevel.Information, Message = "RabbitMQ connection to {server.address}:{server.port} could not be recovered yet: {sanyappc.rabbitmq.connection.recovery.error}")]
    private static partial void LogRecoveryAttemptFailed(
        ILogger logger,
        [TagName("server.address")] string hostname,
        [TagName("server.port")] int port,
        [TagName("sanyappc.rabbitmq.connection.recovery.error")] string reason);

    [LoggerMessage(EventId = 5, Level = LogLevel.Information, Message = "RabbitMQ connection to {server.address}:{server.port} recovered after {sanyappc.rabbitmq.connection.recovery.duration}")]
    private static partial void LogConnectionRecovered(
        ILogger logger,
        [TagName("server.address")] string hostname,
        [TagName("server.port")] int port,
        [TagName("sanyappc.rabbitmq.connection.recovery.duration")] TimeSpan outage);

    private async Task<IConnection> GetOrCreateConnectionAsync(CancellationToken cancellationToken)
    {
        await semaphoreSlim.WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            if (connection is null)
            {
                connection = await connectionFactory.CreateConnectionAsync(cancellationToken)
                    .ConfigureAwait(false);

                connection.ConnectionShutdownAsync += OnConnectionShutdownAsync;
                connection.ConnectionRecoveryErrorAsync += OnConnectionRecoveryErrorAsync;
                connection.RecoverySucceededAsync += OnRecoverySucceededAsync;

                LogConnectionEstablished(logger, connectionFactory.HostName, connectionFactory.Port);
            }

            return connection;
        }
        finally
        {
            semaphoreSlim.Release();
        }
    }

    private Task OnConnectionShutdownAsync(object? sender, ShutdownEventArgs args)
    {
        if (args.Initiator == ShutdownInitiator.Application)
            return Task.CompletedTask;

        Interlocked.CompareExchange(ref connectionLostAt, Stopwatch.GetTimestamp(), 0);
        LogConnectionLost(logger, ServerAddress, ServerPort, args.ReplyText, connectionFactory.NetworkRecoveryInterval);

        return Task.CompletedTask;
    }

    private Task OnConnectionRecoveryErrorAsync(object? sender, ConnectionRecoveryErrorEventArgs args)
    {
        LogRecoveryAttemptFailed(logger, ServerAddress, ServerPort, args.Exception.Message);

        return Task.CompletedTask;
    }

    private Task OnRecoverySucceededAsync(object? sender, AsyncEventArgs args)
    {
        long lostAt = Interlocked.Exchange(ref connectionLostAt, 0);
        if (lostAt == 0)
            return Task.CompletedTask;

        TimeSpan outage = Stopwatch.GetElapsedTime(lostAt);
        LogConnectionRecovered(logger, ServerAddress, ServerPort, outage);
        RabbitMqTelemetry.RecordConnectionRecovery(ServerAddress, ServerPort, outage);

        return Task.CompletedTask;
    }

    public async Task CheckAsync(CancellationToken cancellationToken = default)
    {
        IConnection connection = await GetOrCreateConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        using IChannel channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IChannel> CreateChannelAsync(CancellationToken cancellationToken = default)
    {
        IConnection connection = await GetOrCreateConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        IChannel channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        await channel.BasicQosAsync(0, 1, false, cancellationToken)
           .ConfigureAwait(false);

        return channel;
    }

    public async ValueTask DisposeAsync()
    {
        await semaphoreSlim.WaitAsync()
           .ConfigureAwait(false);

        try
        {
            if (connection is not null)
            {
                await connection.DisposeAsync()
                    .ConfigureAwait(false);

                connection = null;

                LogConnectionDisposed(logger);
            }
        }
        finally
        {
            semaphoreSlim.Release();
            semaphoreSlim.Dispose();
        }

    }
}
