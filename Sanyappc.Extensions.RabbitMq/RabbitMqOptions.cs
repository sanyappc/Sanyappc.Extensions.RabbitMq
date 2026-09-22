using System.ComponentModel.DataAnnotations;

namespace Sanyappc.Extensions.RabbitMq;

public class RabbitMqOptions : IValidatableObject
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

    [Required]
    [Range(1, int.MaxValue)]
    public int RecoveryIntervalInSeconds { get; set; } = 5;

    [Required]
    [Range(1, int.MaxValue)]
    public int RecoveryTimeoutInSeconds { get; set; } = 60;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // The first reconnect attempt starts one interval after the loss, so a timeout that short gives up before any attempt.
        if (RecoveryTimeoutInSeconds <= RecoveryIntervalInSeconds)
            yield return new ValidationResult(
                $"{nameof(RecoveryTimeoutInSeconds)} must be greater than {nameof(RecoveryIntervalInSeconds)}.",
                [nameof(RecoveryTimeoutInSeconds)]);
    }
}
