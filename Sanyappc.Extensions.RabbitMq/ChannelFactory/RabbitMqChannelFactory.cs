using System.Collections.Concurrent;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using RabbitMQ.Client;

namespace Sanyappc.Extensions.RabbitMq
{
    internal class RabbitMqChannelFactory(ILogger<RabbitMqChannelFactory> logger, IOptions<RabbitMqOptions> options) : IRabbitMqChannelFactory, IAsyncDisposable
    {
        private readonly ILogger<RabbitMqChannelFactory> logger = logger;
        private readonly ConcurrentDictionary<string, Task<IConnection>> connections = new();
        private readonly RabbitMqOptions rabbitMqOptions = options.Value;
        private readonly SemaphoreSlim semaphoreSlim = new(1, 1);

        private async Task<IConnection> GetOrCreateConnectionAsync(CancellationToken cancellationToken, string connectionName = null)
        {
            RabbitMqConnectionSettings? connectionSettings;

            if (connectionName == null)
            {
                connectionSettings = rabbitMqOptions.Connection;
                connectionName = "default";
            }
            else if (!rabbitMqOptions.Connections.TryGetValue(connectionName, out connectionSettings))
            {
                throw new KeyNotFoundException($"No RMQ config named \"{connectionName}\"");
            }

            logger.LogInformation("RMQ config for {}", connectionName);

            await semaphoreSlim.WaitAsync(cancellationToken)
                .ConfigureAwait(false);

            try
            {
                var connection = await connections.GetOrAdd(connectionName, name =>
                {
                    ConnectionFactory factory = new ConnectionFactory
                    {
                        HostName = connectionSettings.HostName,
                        Port = connectionSettings.Port,
                        UserName = connectionSettings.UserName,
                        Password = connectionSettings.Password
                    };
                    return factory.CreateConnectionAsync(cancellationToken);
                }).ConfigureAwait(false);

                logger.LogInformation("Created connection to {} : {}", connectionSettings.HostName, connectionSettings.Port);

                return connection;
            }
            finally
            {
                semaphoreSlim.Release();
            }
        }

        public async ValueTask<IChannel> CreateChannelAsync(string connectionName, CancellationToken cancellationToken = default)
        {
            IConnection connection = await GetOrCreateConnectionAsync(cancellationToken, connectionName)
                .ConfigureAwait(false);

            IChannel channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            await channel.BasicQosAsync(0, 1, false, cancellationToken)
               .ConfigureAwait(false);

            return channel;
        }

        public void ClearDeadConnections()
        {
            foreach (var (key, connection) in connections)
            {
                if (!connection.Result.IsOpen)
                {
                    if (connections.TryRemove(key, out var deadConnection))
                        deadConnection.Dispose();
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            await semaphoreSlim.WaitAsync()
               .ConfigureAwait(false);

            try
            {
                foreach (var (_, connection) in connections)
                {
                    if (!connection.IsCompleted)
                        continue;
                    await connection.Result.CloseAsync().ConfigureAwait(false);
                    connection.Dispose();
                }
                connections.Clear();
            }
            finally
            {
                semaphoreSlim.Release();
            }

            GC.SuppressFinalize(this);
        }
    }
}
