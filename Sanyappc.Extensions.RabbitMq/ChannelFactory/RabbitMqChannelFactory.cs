
using Microsoft.Extensions.Logging;

using RabbitMQ.Client;

namespace Sanyappc.Extensions.RabbitMq
{
    internal partial class RabbitMqChannelFactory(ILogger<RabbitMqChannelFactory> logger, RabbitMqOptions options) : IRabbitMqChannelFactory, IAsyncDisposable
    {
        private readonly ILogger<RabbitMqChannelFactory> logger = logger;
        private readonly ConnectionFactory connectionFactory = new()
        {
            HostName = options.Hostname,
            Port = options.Port,
            UserName = options.Username,
            Password = options.Password
        };

        private readonly SemaphoreSlim semaphoreSlim = new(1, 1);
        private volatile IConnection? connection;

        [LoggerMessage(Level = LogLevel.Information, Message = "RabbitMQ connection established to {Hostname}:{Port}")]
        private static partial void LogConnectionEstablished(ILogger logger, string hostname, int port);

        [LoggerMessage(Level = LogLevel.Information, Message = "RabbitMQ connection disposed")]
        private static partial void LogConnectionDisposed(ILogger logger);

        private async ValueTask<IConnection> GetOrCreateConnectionAsync(CancellationToken cancellationToken)
        {
            await semaphoreSlim.WaitAsync(cancellationToken)
                .ConfigureAwait(false);

            try
            {
                if (connection is null)
                {
                    connection = await connectionFactory.CreateConnectionAsync(cancellationToken)
                        .ConfigureAwait(false);

                    LogConnectionEstablished(logger, connectionFactory.HostName, connectionFactory.Port);
                }

                return connection;
            }
            finally
            {
                semaphoreSlim.Release();
            }
        }

        public async ValueTask<IChannel> CreateChannelAsync(CancellationToken cancellationToken = default)
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

            GC.SuppressFinalize(this);
        }
    }
}
