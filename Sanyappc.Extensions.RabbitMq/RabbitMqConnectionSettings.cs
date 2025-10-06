using System.ComponentModel.DataAnnotations;

namespace Sanyappc.Extensions.RabbitMq
{
    public record RabbitMqConnectionSettings
    {
        [Required]
        [MinLength(1)]
        public required string HostName { get; init; }

        [Range(-1, 65536)]
        public int Port { get; init; } = -1;

        [Required]
        [MinLength(1)]
        public required string UserName { get; init; }

        [Required]
        [MinLength(1)]
        public required string Password { get; init; }

        [Required]
        [MinLength(1)]
        public required string QueueName { get; init; }
    }
}
