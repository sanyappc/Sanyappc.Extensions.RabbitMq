using System.ComponentModel.DataAnnotations;

namespace Sanyappc.Extensions.RabbitMq
{
    public record RabbitMqOptions
    {
        [Required]
        public Dictionary<string, RabbitMqConnectionSettings> Connections { get; set; } = new();

        [Required]
        public Dictionary<string, RabbitMqConsumerOptions> Consumers { get; set; } = new();

        [Required]
        public Dictionary<string, RabbitMqPublisherOptions> Publishers { get; set; } = new();
    }
}
