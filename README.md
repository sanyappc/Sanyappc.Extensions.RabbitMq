# Sanyappc.Extensions.RabbitMq

[![NuGet](https://img.shields.io/nuget/v/Sanyappc.Extensions.RabbitMq)](https://www.nuget.org/packages/Sanyappc.Extensions.RabbitMq)

A .NET library for publishing and consuming RabbitMQ messages. Provides typed JSON messaging, manual acknowledgement, request/reply over Direct Reply-to, and built-in distributed tracing.

## Installation

```
dotnet add package Sanyappc.Extensions.RabbitMq
```

## Configuration

The library binds options from the `RabbitMq` configuration section.

### appsettings.json

```json
{
  "RabbitMq": {
    "Hostname": "localhost",
    "Port": -1,
    "Username": "guest",
    "Password": "guest"
  }
}
```

> `Port: -1` uses the RabbitMQ default port (5672).

### Environment variables

Use `__` as the section separator:

```
RabbitMq__Hostname=localhost
RabbitMq__Port=5672
RabbitMq__Username=guest
RabbitMq__Password=guest
```

### Programmatic (code)

```csharp
services.AddRabbitMqService(options =>
{
    options.Hostname = "localhost";
    options.Username = "guest";
    options.Password = "guest";
});
```

Configuration always takes precedence over the delegate. The delegate is useful for defaults in tests or environments without a config file.

## Registration

```csharp
builder.Services.AddRabbitMqService();
```

## Publishing

Inject `IRabbitMqPublishService` and call `PublishAsync`. Messages are serialized as JSON.

```csharp
public class OrderService(IRabbitMqPublishService publisher)
{
    public Task SendOrderAsync(Order order, CancellationToken ct) =>
        publisher.PublishAsync("orders", order, cancellationToken: ct);
}
```

Raw bytes are also supported:

```csharp
await publisher.PublishAsync("orders", bytes, ct);
```

## Consuming

Implement `IRabbitMqMessageProcessingService` for your message handler. The queue is consumed with `prefetch=1` and **manual acknowledgement** — you must call `AckAsync` or `RejectAsync` on every message.

```csharp
public class OrderProcessor : IRabbitMqMessageProcessingService
{
    public async Task ProcessMessageAsync(RabbitMqMessage message, CancellationToken ct)
    {
        Order order = message.GetBody<Order>();

        // process...

        await message.AckAsync(ct);
        // or: await message.RejectAsync(requeue: false, ct);
    }
}
```

Register the consumer in DI. This starts a hosted service that runs for the lifetime of the application:

```csharp
builder.Services.AddRabbitMqConsumer<OrderProcessor>("orders");
```

`AddRabbitMqConsumer` also calls `AddRabbitMqService` internally, so the explicit call is optional when using only consumers.

Multiple queues:

```csharp
builder.Services.AddRabbitMqConsumer<OrderProcessor>("orders");
builder.Services.AddRabbitMqConsumer<PaymentProcessor>("payments");
```

## Request / Reply

For synchronous RPC over RabbitMQ using [Direct Reply-to](https://www.rabbitmq.com/direct-reply-to.html):

**Caller:**

```csharp
InvoiceResponse invoice = await publisher.RequestAsync<OrderRequest, InvoiceResponse>(
    "invoices", new OrderRequest { OrderId = 42 }, cancellationToken: ct);
```

**Handler:**

```csharp
public class InvoiceProcessor : IRabbitMqMessageProcessingService
{
    public async Task ProcessMessageAsync(RabbitMqMessage message, CancellationToken ct)
    {
        OrderRequest request = message.GetBody<OrderRequest>();

        InvoiceResponse response = new() { /* ... */ };

        await message.ReplyAsync(response, cancellationToken: ct);
        await message.AckAsync(ct);
    }
}
```

## OpenTelemetry

### Tracing

The library creates spans for publish, request, and receive operations using the activity source name exposed by `RabbitMqTelemetry.ActivitySourceName`.

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource(RabbitMqTelemetry.ActivitySourceName)
        .AddOtlpExporter());
```

Spans follow [OpenTelemetry messaging semantic conventions](https://opentelemetry.io/docs/specs/semconv/messaging/):

| Operation | Span name | Kind |
|---|---|---|
| `PublishAsync` | `{queue} publish` | Producer |
| `RequestAsync` | `{queue} request` | Client |
| `ConsumeAsync` | `{queue} receive` | Consumer |

### Metrics

The library records messaging metrics using the meter name exposed by `RabbitMqTelemetry.MeterName`.

```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddMeter(RabbitMqTelemetry.MeterName)
        .AddOtlpExporter());
```

| Instrument | Type | Unit | When recorded |
|---|---|---|---|
| `messaging.publish.messages` | Counter | `{message}` | After each `PublishAsync` |
| `messaging.receive.messages` | Counter | `{message}` | On each message delivery |
| `messaging.process.duration` | Histogram | `s` | Time spent in `ProcessMessageAsync` |

All instruments include `messaging.system = "rabbitmq"` and `messaging.destination.name = {queue}` tags.

### Log correlation

Each consumed message opens a logger scope with the following properties, available in structured log sinks (Seq, Loki, Application Insights, etc.):

| Key | Value |
|---|---|
| `Queue` | Queue name |
| `MessageId` | AMQP message ID (null if not set by publisher) |
| `DeliveryTag` | Per-channel delivery sequence number |
| `TraceId` | W3C trace ID |
| `SpanId` | W3C span ID |
