namespace Sanyappc.Extensions.RabbitMq;

public sealed class RabbitMqTimeoutException : RabbitMqException
{
    public RabbitMqTimeoutException()
        : base("The RabbitMQ request timed out waiting for a reply.") { }

    public RabbitMqTimeoutException(string message)
        : base(message) { }

    public RabbitMqTimeoutException(string message, Exception inner)
        : base(message, inner) { }
}
