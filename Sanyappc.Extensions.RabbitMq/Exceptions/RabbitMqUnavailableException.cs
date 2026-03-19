namespace Sanyappc.Extensions.RabbitMq;

public sealed class RabbitMqUnavailableException : RabbitMqException
{
    public RabbitMqUnavailableException(string message)
        : base(message) { }

    public RabbitMqUnavailableException(string message, Exception inner)
        : base(message, inner) { }
}
