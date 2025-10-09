# Sanyappc.Extensions.RabbitMq

## 1. Configuration setup

### appsettings.json
Declare named connections for each publisher and consumer under `RabbitMq.Connections`:

```json
"RabbitMq": {
  "Connections": {
    "PublisherTasks": {
      "RabbitMqHostName": "localhost",
      "RabbitMqPort": 5672,
      "RabbitMqUserName": "admin1",
      "RabbitMqPassword": "qwerty1",
      "QueueName": "gmail-message-send"
    },
    "ConsumerTasks": {
      "RabbitMqHostName": "localhost",
      "RabbitMqPort": 5672,
      "RabbitMqUserName": "admin1",
      "RabbitMqPassword": "qwerty1",
      "QueueName": "gmail-message-send"
    },
    "ConsumerRegistrations": {
      "RabbitMqHostName": "localhost",
      "RabbitMqPort": 5673,
      "RabbitMqUserName": "admin2",
      "RabbitMqPassword": "qwerty2",
      "QueueName": "registration-confirmation-put"
    }
  }
},
```

### env
```.env
DA_RABBITMQ__CONNECTIONS__CONSUMERTASKS__HOSTNAME  = "localhost"
DA_RABBITMQ__CONNECTIONS__CONSUMERTASKS__PORT      = "5672"
DA_RABBITMQ__CONNECTIONS__CONSUMERTASKS__USERNAME  = "admin1"
DA_RABBITMQ__CONNECTIONS__CONSUMERTASKS__PASSWORD  = "qwerty1"
DA_RABBITMQ__CONNECTIONS__CONSUMERTASKS__QUEUENAME = "gmail-message-send"

DA_RABBITMQ__CONNECTIONS__CONSUMERREGISTRATIONS__HOSTNAME  = "localhost"
DA_RABBITMQ__CONNECTIONS__CONSUMERREGISTRATIONS__PORT      = "5673"
DA_RABBITMQ__CONNECTIONS__CONSUMERREGISTRATIONS__USERNAME  = "admin2"
DA_RABBITMQ__CONNECTIONS__CONSUMERREGISTRATIONS__PASSWORD  = "qwerty2"
DA_RABBITMQ__CONNECTIONS__CONSUMERREGISTRATIONS__QUEUENAME = "registration-confirmation-put"

builder.Configuration.AddEnvironmentVariables("DA_");
```

Or, for a single connection, options can be declared simply:
```.env
DA_RABBITMQ__CONNECTION__HOSTNAME  = "localhost"
DA_RABBITMQ__CONNECTION__PORT      = "5672"
DA_RABBITMQ__CONNECTION__USERNAME  = "admin1"
DA_RABBITMQ__CONNECTION__PASSWORD  = "qwerty1"
DA_RABBITMQ__CONNECTION__QUEUENAME = "gmail-message-send"
```

## 2. Usage (consumers)
### Core RabbutMq services Registration
```csharp
// Register core rabbit client infrastructure
builder.Services.AddNamedRabbitMqService(builder.Configuration, "RABBITMQ");

// Register message processor
builder.Services.AddSingleton<IRabbitMqMessageProcessingService, RabbitMqMessageProcessingService>();

```

### Consumers via injected factory
```csharp
public class Worker(IConsumerFactory consumerFactory) : BackgroundService
{
    private readonly IConsumerFactory consumerFactory = consumerFactory;

    private IRabbitConsumer rabbitMqTasksConsumerService = null!;
    private IRabbitConsumer rabbitMqRegistrationsConsumerService = null!;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (rabbitMqTasksConsumerService is null)
            rabbitMqTasksConsumerService = await consumerFactory.BuildAsync(stoppingToken, "ConsumerTasks")
                .ConfigureAwait(false);

        if (rabbitMqRegistrationsConsumerService is null)
            rabbitMqRegistrationsConsumerService = await consumerFactory.BuildAsync(stoppingToken, "ConsumerRegistrations")
                .ConfigureAwait(false);

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.WhenAll(
                rabbitMqTasksConsumerService.ExecuteAsync<IRabbitMqMessageProcessingService>(stoppingToken),
                rabbitMqRegistrationsConsumerService.ExecuteAsync<IRabbitMqMessageProcessingService>(stoppingToken)
            );

            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
    }
}
```

Or, for a single connection of a ```DA_RABBITMQ__CONNECTION__HOSTNAME  = "localhost"``` configuration format:
```csharp
rabbitMqTasksConsumerService = await consumerFactory.BuildAsync(stoppingToken);
```


## 3. Usage (publish, with extra service)
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

### Publisher via injected factory in the service
```csharp
public class GmailMessageSendRabbitMqService(IPublisherFactory rabbitMqPublishFactory, IOptions<GmailMessageSendRabbitMqOptions> options) : IGmailMessageSendRabbitMqService
{
    private IPublisherFactory rabbitMqPublishFactory = rabbitMqPublishFactory;
    private readonly bool send = options.Value.RabbitMqGmailMessageSend;
    private IRabbitMqPublisher rabbitMqPublishService = null!;

    public async Task SendAccountCreateConfirmationAsync(string publisherName, string email, string confirmationToken, CancellationToken cancellationToken = default)
    {
        if (rabbitMqPublishService == null)
            rabbitMqPublishService = await rabbitMqPublishFactory.BuildAsync(publisherName, cancellationToken)
                .ConfigureAwait(false);

        if (send)
        {
            await rabbitMqPublishService.PublishAsync(
                new
                {
                    type = options.Value.RabbitMqGmailMessageType,
                    to = new string[] { email },
                    token = confirmationToken,
                },
                cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
```

### Register publishing services
```csharp
// Add options for each service
builder.Services.AddOptions<GmailMessageSendRabbitMqOptions>()
    .Bind(builder.Configuration)
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<RegistrationPutRabbitMqOptions>()
    .Bind(builder.Configuration)
    .ValidateDataAnnotations()
    .ValidateOnStart();

// Register publishing sevices
builder.Services.AddSingleton<IGmailMessageSendRabbitMqService, GmailMessageSendRabbitMqService>();
builder.Services.AddSingleton<IRegistrationPutRabbitMqService, RegistrationPutRabbitMqService>();

// Register core rabbit client infrastructure
builder.Services.AddNamedRabbitMqService(builder.Configuration, "RabbitMq");
```

### Use publishing services in worker
```csharp
public class WorkerTask(IGmailMessageSendRabbitMqService gmailMessageSendRabbitMqService) : IWorkerTask
{
    private readonly IGmailMessageSendRabbitMqService gmailMessageSendRabbitMqService = gmailMessageSendRabbitMqService;

    public async Task WorkAsync(CancellationToken cancellationToken = default)
    {
        await gmailMessageSendRabbitMqService.SendAccountCreateConfirmationAsync("GmailMessageSend", "mail@x.com", "gmailMessageToken", cancellationToken)
            .ConfigureAwait(false);
    }
}
```