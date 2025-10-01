# Sanyappc.Extensions.RabbitMq

## 1. Configuration setup

### appsettings.json
Declare your named connections under `RabbitMq.Connections` and consumers, if needed any, under `RabbitMq.Consumers`:

```json
"RabbitMq": {
  "Connections": {
    "Tasks": {
      "RabbitMqHostName": "localhost",
      "RabbitMqPort": 5672,
      "RabbitMqUserName": "admin1",
      "RabbitMqPassword": "qwerty1"
    },
    "Registrations": {
      "RabbitMqHostName": "localhost",
      "RabbitMqPort": 5673,
      "RabbitMqUserName": "admin2",
      "RabbitMqPassword": "qwerty2"
    }
  },
  "Consumers": {
    "GmailMessages": {
      "Name": "GmailMessages",
      "QueueName": "amadesci-gmail-message-send",
      "ConnectionName": "Tasks"
    },
    "RegistrationNotify": {
      "Name": "RegistrationNotifications",
      "QueueName": "amadesci-registration-confirmation-put",
      "ConnectionName": "Registrations"
    }
  }
},
```

## 2. Usage (consumers via factory)
### Consumers creation
```csharp
internal class RabbitMqRegistrationsConsumerService : RabbitConsumer
{
    public RabbitMqRegistrationsConsumerService(
        ILogger<RabbitMqRegistrationsConsumerService> logger,
        IRabbitMqChannelFactory rabbitMqChannelFactory,
        IServiceScopeFactory serviceScopeFactory,
        string connectionName,
        string queue) : base(logger, rabbitMqChannelFactory, serviceScopeFactory, connectionName, queue)
    {

    }
}
```

### Service Registration
```csharp
// Register core rabbit client infrastructure
builder.Services.AddNamedRabbitMqService(builder.Configuration, "RabbitMq");

// Register message processor
builder.Services.AddSingleton<IRabbitMqMessageProcessingService, RabbitMqMessageProcessingService>();

```

### Instantiate consumers via injected factory for usage
```csharp
public class AppWorker(IConsumerFactory consumerFactory) : BackgroundService
{
    private readonly IConsumerFactory consumerFactory = consumerFactory;

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                RabbitMqTasksConsumerService         rabbitMqTasksConsumerService         = consumerFactory.Build<RabbitMqTasksConsumerService>("GmailMessages");
                RabbitMqRegistrationsConsumerService rabbitMqRegistrationsConsumerService = consumerFactory.Build<RabbitMqRegistrationsConsumerService>("RegistrationNotify");

                await Task.WhenAll(
                    rabbitMqTasksConsumerService.ExecuteAsync<IRabbitMqMessageProcessingService>(stoppingToken),
                    rabbitMqRegistrationsConsumerService.ExecuteAsync<IRabbitMqMessageProcessingService>(stoppingToken)
                );

                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
        }
        catch (Exception e)
        {
            //
        }
    }
}
```

## 3. Usage (publish)
### Create services interfaces
```csharp
public interface IGmailMessageSendRabbitMqService
{
    Task SendAccountCreateConfirmationAsync(CancellationToken cancellationToken = default);
}

public interface IRegistrationPutRabbitMqService
{
    Task PutRegistrationConfirmationAsync(CancellationToken cancellationToken = default);
}
```

### Register publish services
```csharp
// Add options for each service if needed
builder.Services.AddOptions<GmailMessageSendRabbitMqOptions>()
    .Bind(builder.Configuration)
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<RegistrationPutRabbitMqOptions>()
    .Bind(builder.Configuration)
    .ValidateDataAnnotations()
    .ValidateOnStart();

// Register sevices
builder.Services.AddSingleton<IGmailMessageSendRabbitMqService, GmailMessageSendRabbitMqService>();
builder.Services.AddSingleton<IRegistrationPutRabbitMqService, RegistrationPutRabbitMqService>();

builder.Services.AddNamedRabbitMqService(builder.Configuration, "RabbitMq");
```

### Inject and use publisher in the service
```csharp
public class GmailMessageSendRabbitMqService(
    IRabbitMqPublishService rabbitMqPublishService,
    IOptions<GmailMessageSendRabbitMqOptions> options) : IGmailMessageSendRabbitMqService
{
    private readonly IRabbitMqPublishService rabbitMqPublishService = rabbitMqPublishService;

    public async Task SendAccountCreateConfirmationAsync(CancellationToken cancellationToken = default)
    {
        await rabbitMqPublishService.PublishAsync(
            options.Value.GmailMessageSendRabbitMqConnectionName,
            options.Value.GmailMessageSendRabbitMqQueue,
            ...
        ).ConfigureAwait(false);
    }
}
```