namespace Sanyappc.Extensions.RabbitMq
{
    public record RabbitMqPublisherOptions
    {
        public string Name { get; init; } = null!;
        public string ConnectionName { get; init; } = null!;
        public string QueueName { get; init; } = null!;
    }
}
