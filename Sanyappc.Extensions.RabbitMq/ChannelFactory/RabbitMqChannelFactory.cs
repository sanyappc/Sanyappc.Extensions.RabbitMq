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

    [LoggerMessage(Level = LogLevel.Information, Message = "RabbitMQ connection established to {Hostname}:{Port}")]
    private static partial void LogConnectionEstablished(ILogger logger, string hostname, int port);

    [LoggerMessage(Level = LogLevel.Information, Message = "RabbitMQ connection disposed")]
    private static partial void LogConnectionDisposed(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "RabbitMQ connection to {Hostname}:{Port} lost: {Reason}. Reconnecting every {RecoveryInterval}")]
    private static partial void LogConnectionLost(ILogger logger, string hostname, int port, string reason, TimeSpan recoveryInterval);

    [LoggerMessage(Level = LogLevel.Warning, Message = "RabbitMQ connection to {Hostname}:{Port} could not be recovered yet")]
    private static partial void LogRecoveryAttemptFailed(ILogger logger, string hostname, int port, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "RabbitMQ connection to {Hostname}:{Port} recovered after {Outage}")]
    private static partial void LogConnectionRecovered(ILogger logger, string hostname, int port, TimeSpan outage);

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
        LogRecoveryAttemptFailed(logger, ServerAddress, ServerPort, args.Exception);

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
