using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Sanyappc.Extensions.RabbitMq
{
    internal class RabbitMqService(ILogger<RabbitMqService> logger, IOptions<RabbitMqOptions> options) : IRabbitMqService, IAsyncDisposable
    {
        private readonly ILogger<RabbitMqService> logger = logger;
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
                if (connection is not null)
                {
                    if (connection.IsOpen)
                        return connection;
                    else
                    {
                        await connection.DisposeAsync()
                            .ConfigureAwait(false);

                        connection = null;
                    }
                }

                connection = await connectionFactory.CreateConnectionAsync(cancellationToken)
                    .ConfigureAwait(false);

                return connection;
            }
            finally
            {
                semaphoreSlim.Release();
            }
        }

        private async ValueTask<IChannel> CreateChannel(CancellationToken cancellationToken = default)
        {
            IConnection connection = await GetOrCreateConnectionAsync(cancellationToken)
                .ConfigureAwait(false);

            IChannel channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            await channel.BasicQosAsync(0, 1, false, cancellationToken)
               .ConfigureAwait(false);

            return channel;
        }

        private async ValueTask<IChannel> CreateQueueAsync(IChannel channel, string queue, CancellationToken cancellationToken = default)
        {
            await channel.QueueDeclareAsync(queue, false, false, false, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return channel;
        }

        internal static T DeserializeMessage<T>(ReadOnlySpan<byte> message)
        {
            return JsonSerializer.Deserialize<T>(message) ?? throw new NotSupportedException();
        }

        internal static byte[] SerializeMessage<T>(T message)
        {
            return JsonSerializer.SerializeToUtf8Bytes(message);
        }

        public async ValueTask PublishAsync<T>(string queue, T message, CancellationToken cancellationToken = default)
        {
            using IChannel channel = await CreateChannel(cancellationToken)
                .ConfigureAwait(false);

            await CreateQueueAsync(channel, queue, cancellationToken)
               .ConfigureAwait(false);

            byte[] body = SerializeMessage(message);

            await channel.BasicPublishAsync(string.Empty, queue, body, cancellationToken)
              .ConfigureAwait(false);
        }

        public async ValueTask<TOut> PublishAsync<TIn, TOut>(string queue, TIn message, CancellationToken cancellationToken = default)
        {
            using IChannel channel = await CreateChannel(cancellationToken)
                .ConfigureAwait(false);

            QueueDeclareOk replyQueue = await channel.QueueDeclareAsync()
                .ConfigureAwait(false);

            string correlationID = $"{Guid.NewGuid()}";

            Channel<ResultOrException<TOut>> messages = Channel.CreateBounded<ResultOrException<TOut>>(1);

            AsyncEventingBasicConsumer consumer = new(channel);
            consumer.ReceivedAsync += async (object sender, BasicDeliverEventArgs @event) =>
            {
                try
                {
                    if (@event.BasicProperties.CorrelationId != correlationID)
                        throw new NotSupportedException();

                    TOut data = DeserializeMessage<TOut>(@event.Body.Span);

                    await messages.Writer.WriteAsync(new ResultOrException<TOut>(data), cancellationToken)
                        .ConfigureAwait(false);
                    messages.Writer.Complete();

                    await channel.BasicAckAsync(@event.DeliveryTag, false, cancellationToken)
                       .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    await messages.Writer.WriteAsync(new ResultOrException<TOut>(ex), cancellationToken)
                       .ConfigureAwait(false);
                    messages.Writer.Complete();

                    await channel.BasicRejectAsync(@event.DeliveryTag, false, cancellationToken)
                        .ConfigureAwait(false);
                }
            };

            await channel.BasicConsumeAsync(replyQueue.QueueName, false, consumer, cancellationToken)
                .ConfigureAwait(false);

            BasicProperties properties = new()
            {
                CorrelationId = correlationID,
                ReplyTo = replyQueue.QueueName
            };

            byte[] body = SerializeMessage(message);

            await channel.BasicPublishAsync(string.Empty, queue, false, properties, body, cancellationToken)
                .ConfigureAwait(false);

            ResultOrException<TOut> resultOrException = await messages.Reader.ReadAsync(cancellationToken)
                .ConfigureAwait(false);

            return resultOrException.Result;
        }

        public async IAsyncEnumerable<RabbitMqMessage<T>> ConsumeAsync<T>(string queue, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            using IChannel channel = await CreateChannel(cancellationToken)
                .ConfigureAwait(false);

            await CreateQueueAsync(channel, queue, cancellationToken)
                .ConfigureAwait(false);

            using BlockingCollection<RabbitMqMessage<T>> messages = new(1);

            AsyncEventingBasicConsumer consumer = new(channel);
            consumer.ReceivedAsync += async (object sender, BasicDeliverEventArgs @event) =>
            {
                //using IDisposable? scope = logger.BeginScope(@event.DeliveryTag);

                try
                {
                    T data = DeserializeMessage<T>(@event.Body.Span);

                    messages.Add(new RabbitMqMessage<T>(data, channel, @event.DeliveryTag, @event.BasicProperties.CorrelationId, @event.BasicProperties.ReplyTo));
                }
                catch (Exception ex)
                {
                    await channel.BasicRejectAsync(@event.DeliveryTag, false, cancellationToken)
                        .ConfigureAwait(false);
                }
            };

            await channel.BasicConsumeAsync(queue, false, consumer, cancellationToken)
                .ConfigureAwait(false);

            foreach (RabbitMqMessage<T> message in messages.GetConsumingEnumerable(cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return message;
            }
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

        private record ResultOrException<T>
        {
            private readonly T? result;
            private readonly Exception? exception;

            public ResultOrException(T result)
            {
                this.result = result;
            }

            public ResultOrException(Exception exception)
            {
                this.exception = exception;
            }

            public T Result => result ?? throw (exception ?? new InvalidOperationException());
        }
    }
}
