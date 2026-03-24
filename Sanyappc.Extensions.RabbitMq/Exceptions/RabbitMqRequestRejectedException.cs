namespace Sanyappc.Extensions.RabbitMq;

public sealed class RabbitMqRequestRejectedException : RabbitMqException
{
    public RabbitMqRequestRejectedException(string message)
        : base(message) { }
}
