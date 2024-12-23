﻿using System.Diagnostics;
using System.Text.Json;

using RabbitMQ.Client;

namespace Sanyappc.Extensions.RabbitMq
{
    public static class RabbitMqMessageExtensions
    {
        public static async ValueTask AckAsync(this RabbitMqMessage message, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(message);

            await message.Channel.BasicAckAsync(message.Event.DeliveryTag, false, cancellationToken)
                .ConfigureAwait(false);
        }

        public static async ValueTask RejectAsync(this RabbitMqMessage message, bool requeue, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(message);

            await message.Channel.BasicRejectAsync(message.Event.DeliveryTag, requeue, cancellationToken)
                .ConfigureAwait(false);
        }

        public static async ValueTask ReplyAsync(this RabbitMqMessage message, ReadOnlyMemory<byte> body, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(message);

            string replyTo = message.Event.BasicProperties.ReplyTo
                ?? throw new ArgumentException("reply-to is null", nameof(message));

            BasicProperties properties = new();
            properties.Inject(Activity.Current);

            string? correlationId = message.Event.BasicProperties.CorrelationId;
            if (correlationId is not null)
                properties.CorrelationId = correlationId;

            await message.Channel.BasicPublishAsync(string.Empty, replyTo, false, properties, body, cancellationToken)
                .ConfigureAwait(false);
        }

        public static async ValueTask ReplyAsync<T>(this RabbitMqMessage message, T body, JsonSerializerOptions? options = null, CancellationToken cancellationToken = default)
        {
            await message.ReplyAsync(RabbitMqMessage.SerializeBody(body, options), cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
