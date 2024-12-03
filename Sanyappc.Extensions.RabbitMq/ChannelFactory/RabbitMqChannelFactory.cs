
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using RabbitMQ.Client;

namespace Sanyappc.Extensions.RabbitMq
{
    internal class RabbitMqChannelFactory(ILogger<RabbitMqChannelFactory> logger, IOptions<RabbitMqOptions> options) : IRabbitMqChannelFactory, IAsyncDisposable
    {
        private readonly ILogger<RabbitMqChannelFactory> logger = logger;
        private readonly ConnectionFactory connectionFactory = new()
        {
            HostName = options.Value.RabbitMqHostname,
            Port = options.Value.RabbitMqPort,
            UserName = options.Value.RabbitMqUsername,
            Password = options.Value.RabbitMqPassword
        };

        private readonly SemaphoreSlim semaphoreSlim = new(1, 1);
        private volatile IConnection? connection;

        private async ValueTask<IConnection> GetOrCreateConnectionAsync(CancellationToken cancellationToken)
        {
            await semaphoreSlim.WaitAsync(cancellationToken)
                .ConfigureAwait(false);

            try
            {
                connection ??= await connectionFactory.CreateConnectionAsync(cancellationToken)
                    .ConfigureAwait(false);

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
                }
            }
            finally
            {
                semaphoreSlim.Release();
            }

            GC.SuppressFinalize(this);
        }
    }
}
