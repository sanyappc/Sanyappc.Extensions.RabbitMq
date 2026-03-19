using System.ComponentModel.DataAnnotations;

namespace Sanyappc.Extensions.RabbitMq;

public class RabbitMqOptions
{
    [Required]
    [MinLength(1)]
    public string Hostname { get; set; } = string.Empty;

    [Range(-1, 65536)]
    public int Port { get; set; } = -1;

    [Required]
    [MinLength(1)]
    public string Username { get; set; } = string.Empty;

    [Required]
    [MinLength(1)]
    public string Password { get; set; } = string.Empty;

    [Required]
    [Range(-1, int.MaxValue)]
    public int ReplyTimeoutInSeconds { get; set; } = 5;
}
