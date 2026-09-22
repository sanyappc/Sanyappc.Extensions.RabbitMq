using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Sanyappc.Extensions.RabbitMq.Tests;

public sealed class RabbitMqOptionsTests
{
    private static readonly Dictionary<string, string?> RequiredSettings = new()
    {
        ["RabbitMq:Hostname"] = "localhost",
        ["RabbitMq:Username"] = "guest",
        ["RabbitMq:Password"] = "guest"
    };

    private static RabbitMqOptions Resolve(IEnumerable<KeyValuePair<string, string?>> settings, Action<RabbitMqOptions>? configure = null)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(RequiredSettings)
            .AddInMemoryCollection(settings)
            .Build();

        ServiceCollection services = new();
        services.AddSingleton(configuration);
        services.AddRabbitMqService(configure);

        using ServiceProvider provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<RabbitMqOptions>>().Value;
    }

    private static RabbitMqOptions ResolveWith(Action<RabbitMqOptions> configure) => Resolve([], configure);

    [Fact]
    public void TheDefaultsAreValid()
    {
        RabbitMqOptions options = Resolve([]);

        Assert.Equal(TimeSpan.FromSeconds(5), options.ReplyTimeout);
        Assert.Equal(TimeSpan.FromSeconds(5), options.RecoveryInterval);
        Assert.Equal(TimeSpan.FromMinutes(1), options.RecoveryTimeout);
    }

    [Fact]
    public void TimeoutsBindFromConfigurationStrings()
    {
        Dictionary<string, string?> settings = new()
        {
            ["RabbitMq:ReplyTimeout"] = "-00:00:00.001",
            ["RabbitMq:RecoveryInterval"] = "00:00:02",
            ["RabbitMq:RecoveryTimeout"] = "00:02:00"
        };

        RabbitMqOptions options = Resolve(settings);

        Assert.Equal(Timeout.InfiniteTimeSpan, options.ReplyTimeout);
        Assert.Equal(TimeSpan.FromSeconds(2), options.RecoveryInterval);
        Assert.Equal(TimeSpan.FromMinutes(2), options.RecoveryTimeout);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void ANonPositiveReplyTimeoutIsRejected(int seconds)
    {
        OptionsValidationException failure = Assert.Throws<OptionsValidationException>(
            () => ResolveWith(options => options.ReplyTimeout = TimeSpan.FromSeconds(seconds)));

        Assert.Contains(nameof(RabbitMqOptions.ReplyTimeout), failure.Message);
    }

    [Fact]
    public void AReplyTimeoutLongerThanATimerCanWaitIsRejected()
    {
        OptionsValidationException failure = Assert.Throws<OptionsValidationException>(
            () => ResolveWith(options => options.ReplyTimeout = TimeSpan.FromDays(60)));

        Assert.Contains(nameof(RabbitMqOptions.ReplyTimeout), failure.Message);
    }

    [Fact]
    public void ARecoveryTimeoutNoLongerThanTheIntervalIsRejected()
    {
        OptionsValidationException failure = Assert.Throws<OptionsValidationException>(() => ResolveWith(options =>
        {
            options.RecoveryInterval = TimeSpan.FromSeconds(5);
            options.RecoveryTimeout = TimeSpan.FromSeconds(5);
        }));

        Assert.Contains(nameof(RabbitMqOptions.RecoveryTimeout), failure.Message);
    }
}
