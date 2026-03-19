namespace Sanyappc.Extensions.RabbitMq;

public abstract class RabbitMqException : Exception
{
    protected RabbitMqException(string message)
        : base(message) { }

    protected RabbitMqException(string message, Exception inner)
        : base(message, inner) { }
}
