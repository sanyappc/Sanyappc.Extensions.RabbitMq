using System.ComponentModel.DataAnnotations;

namespace Sanyappc.Extensions.RabbitMq
{
    public record RabbitMqOptions
    {
        [Required]
        public Dictionary<string, RabbitMqConnectionSettings> Connections { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public RabbitMqConnectionSettings? Connection { get; set; }
    }
}
