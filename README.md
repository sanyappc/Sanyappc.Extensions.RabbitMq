# Sanyappc.Extensions.RabbitMq
## 1. Configuration setup
### appsettings.json
Declare named connections under `RabbitMq.Connections`, publishers and consumers under `RabbitMq.Consumers` and `RabbitMq.Publishers`:

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
  },
  "Publishers": {
    "GmailMessageSend": {
      "Name": "GmailMessageSend",
      "ConnectionName": "Tasks",
      "QueueName": "amadesci-gmail-message-send"
    },
    "RegistrationNotify": {
      "Name": "RegistrationNotify",
      "ConnectionName": "Registrations",
      "QueueName": "amadesci-registration-confirmation-put"
    }
  }
},
```
Consumers' and publishers' ConnectionName must correlate with connections names.

### env
```csharp
DA__RABBITMQ__CONNECTIONS__TASKS__RABBITMQHOSTNAME = "localhost"
DA__RABBITMQ__CONNECTIONS__TASKS__RABBITMQPORT     = "5672"
DA__RABBITMQ__CONNECTIONS__TASKS__RABBITMQUSERNAME = "admin1"
DA__RABBITMQ__CONNECTIONS__TASKS__RABBITMQPASSWORD = "qwerty1"

DA__RABBITMQ__CONNECTIONS__REGISTRATIONS__RABBITMQHOSTNAME = "localhost"
DA__RABBITMQ__CONNECTIONS__REGISTRATIONS__RABBITMQPORT     = "5673"
DA__RABBITMQ__CONNECTIONS__REGISTRATIONS__RABBITMQUSERNAME = "admin2"
DA__RABBITMQ__CONNECTIONS__REGISTRATIONS__RABBITMQPASSWORD = "qwerty2"

DA__RABBITMQ__CONSUMERS__GMAILMESSAGES__NAME           = "GmailMessages"
DA__RABBITMQ__CONSUMERS__GMAILMESSAGES__QUEUENAME      = "amadesci-gmail-message-send"
DA__RABBITMQ__CONSUMERS__GMAILMESSAGES__CONNECTIONNAME = "Tasks"

DA__RABBITMQ__PUBLISHERS__GMAILMESSAGESEND__NAME           = "GmailMessageSend"
DA__RABBITMQ__PUBLISHERS__GMAILMESSAGESEND__QUEUENAME      = "amadesci-gmail-message-send"
DA__RABBITMQ__PUBLISHERS__GMAILMESSAGESEND__CONNECTIONNAME = "Tasks"

builder.Configuration.AddEnvironmentVariables("DA_");
```

## 2. Usage (consumers)
### Core RabbutMq services Registration
```csharp
// Register core rabbit client infrastructure
builder.Services.AddNamedRabbitMqService(builder.Configuration, "RabbitMq");

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
            rabbitMqTasksConsumerService = await consumerFactory.BuildAsync("GmailMessages", stoppingToken)
                .ConfigureAwait(false);

        if (rabbitMqRegistrationsConsumerService is null)
            rabbitMqRegistrationsConsumerService = await consumerFactory.BuildAsync("RegistrationNotify", stoppingToken)
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