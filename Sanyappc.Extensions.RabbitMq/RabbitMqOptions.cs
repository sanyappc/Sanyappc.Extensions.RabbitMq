using System.ComponentModel.DataAnnotations;

namespace Sanyappc.Extensions.RabbitMq;

public class RabbitMqOptions : IValidatableObject
{
    // CancelAfter accepts nothing longer.
    private static readonly TimeSpan MaxReplyTimeout = TimeSpan.FromMilliseconds(uint.MaxValue - 1);

    // SemaphoreSlim.WaitAsync, which times the wait for a recovery, accepts nothing longer.
    private static readonly TimeSpan MaxRecoveryTimeout = TimeSpan.FromMilliseconds(int.MaxValue);

    private const string InfiniteTimeSpanInConfiguration = "-00:00:00.001";

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

    public TimeSpan ReplyTimeout { get; set; } = TimeSpan.FromSeconds(5);

    public TimeSpan RecoveryInterval { get; set; } = TimeSpan.FromSeconds(5);

    public TimeSpan RecoveryTimeout { get; set; } = TimeSpan.FromMinutes(1);

    private IEnumerable<ValidationResult> ValidateReplyTimeout()
    {
        if (ReplyTimeout == Timeout.InfiniteTimeSpan)
            yield break;

        if (ReplyTimeout <= TimeSpan.Zero)
            yield return new ValidationResult(
                $"{nameof(ReplyTimeout)} must be positive, or {nameof(Timeout)}.{nameof(Timeout.InfiniteTimeSpan)} ({InfiniteTimeSpanInConfiguration} in configuration) to wait for a reply forever.",
                [nameof(ReplyTimeout)]);

        if (ReplyTimeout > MaxReplyTimeout)
            yield return new ValidationResult($"{nameof(ReplyTimeout)} must not exceed {MaxReplyTimeout}.", [nameof(ReplyTimeout)]);
    }

    private IEnumerable<ValidationResult> ValidateRecovery()
    {
        if (RecoveryInterval <= TimeSpan.Zero)
            yield return new ValidationResult($"{nameof(RecoveryInterval)} must be positive.", [nameof(RecoveryInterval)]);

        if (RecoveryTimeout > MaxRecoveryTimeout)
            yield return new ValidationResult($"{nameof(RecoveryTimeout)} must not exceed {MaxRecoveryTimeout}.", [nameof(RecoveryTimeout)]);

        if (RecoveryTimeout <= RecoveryInterval)
        {
            yield return new ValidationResult(
                $"{nameof(RecoveryTimeout)} ({RecoveryTimeout}) must be greater than {nameof(RecoveryInterval)} ({RecoveryInterval}): "
                + "the first reconnect attempt starts one interval after the loss, so the consumer would give up before it.",
                [nameof(RecoveryTimeout)]);
        }
    }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (ValidationResult result in ValidateReplyTimeout())
            yield return result;

        foreach (ValidationResult result in ValidateRecovery())
            yield return result;
    }
}
