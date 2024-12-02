using RabbitMQ.Client;

namespace Sanyappc.Extensions.RabbitMq
{
    public record RabbitMqMessage<T>(T data, IChannel channel, ulong deliveryTag, string? correlationID, string? replyTo)
    {
        private readonly IChannel channel = channel;
        private readonly ulong deliveryTag = deliveryTag;
        private readonly string? correlationID = correlationID;
        private readonly string? replyTo = replyTo;

        public T Data { get; init; } = data;

        private async ValueTask ReplyIfNeededAsync<TOut>(TOut? message, CancellationToken cancellationToken)
        {
            if (replyTo is not null)
            {
                byte[] body = RabbitMqService.SerializeMessage(message);

                if (correlationID is not null)
                {
                    BasicProperties properties = new()
                    {
                        CorrelationId = correlationID
                    };

                    await channel.BasicPublishAsync(string.Empty, replyTo, false, properties, body, cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    await channel.BasicPublishAsync(string.Empty, replyTo, body, cancellationToken)
                       .ConfigureAwait(false);
                }
            }
        }

        public async ValueTask AckAsync(CancellationToken cancellationToken = default)
        {
            await channel.BasicAckAsync(deliveryTag, false, cancellationToken)
                .ConfigureAwait(false);
        }

        public async ValueTask AckAsync<TOut>(TOut? replyMessage, CancellationToken cancellationToken = default)
        {
            await AckAsync(cancellationToken)
                .ConfigureAwait(false);

            await ReplyIfNeededAsync(replyMessage, cancellationToken)
                .ConfigureAwait(false);
        }

        public async ValueTask RejectAsync(CancellationToken cancellationToken = default)
        {
            await channel.BasicRejectAsync(deliveryTag, false, cancellationToken)
                .ConfigureAwait(false);
        }

        public async ValueTask RejectAsync<TOut>(TOut? replyMessage, CancellationToken cancellationToken = default)
        {
            await RejectAsync(cancellationToken)
                .ConfigureAwait(false);

            await ReplyIfNeededAsync(replyMessage, cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
