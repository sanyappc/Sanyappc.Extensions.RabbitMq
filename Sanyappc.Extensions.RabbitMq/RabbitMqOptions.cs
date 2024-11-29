using System.ComponentModel.DataAnnotations;

namespace Sanyappc.Extensions.RabbitMq
{
    internal record RabbitMqOptions
    {
        [Required]
        [MinLength(1)]
        public required string RabbitMqHostname { get; init; }

        [Range(-1, 65536)]
        public int RabbitMqPort { get; init; } = -1;

        [Required]
        [MinLength(1)]
        public required string RabbitMqUsername { get; init; }

        [Required]
        [MinLength(1)]
        public required string RabbitMqPassword { get; init; }
    }
}
