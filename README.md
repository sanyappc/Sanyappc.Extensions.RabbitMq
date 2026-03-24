# Sanyappc.Extensions.RabbitMq

[![NuGet](https://img.shields.io/nuget/v/Sanyappc.Extensions.RabbitMq)](https://www.nuget.org/packages/Sanyappc.Extensions.RabbitMq)

A .NET library for publishing and consuming RabbitMQ messages. Supports typed JSON messaging, manual acknowledgement, request/reply via Direct Reply-to, configurable reply timeout, multiple broker connections, well-typed exceptions, health checks, and built-in OpenTelemetry tracing and metrics following messaging semantic conventions.

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
    "Password": "guest",
    "ReplyTimeoutInSeconds": 5
  }
}
```

> `Port: -1` uses the RabbitMQ default port (5672).
> `ReplyTimeoutInSeconds: -1` disables the timeout (waits indefinitely). Default is `5`.

### Environment variables

Use `__` as the section separator:

```
RabbitMq__Hostname=localhost
RabbitMq__Port=5672
RabbitMq__Username=guest
RabbitMq__Password=guest
RabbitMq__ReplyTimeoutInSeconds=30
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

The delegate takes precedence over configuration. It is useful for overriding specific values regardless of the config file.

## Registration

```csharp
builder.Services.AddRabbitMqService();
```

## Multiple brokers

To connect to more than one RabbitMQ broker, use the named overload. Each name registers an independent set of keyed services.

```csharp
builder.Services.AddRabbitMqService("broker1", o => o.Hostname = "rabbit1");
builder.Services.AddRabbitMqService("broker2", o => o.Hostname = "rabbit2");
```

Config binding uses `RabbitMq:{name}` as the section:

```json
{
  "RabbitMq": {
    "broker1": { "Hostname": "rabbit1", "Username": "guest", "Password": "guest" },
    "broker2": { "Hostname": "rabbit2", "Username": "guest", "Password": "guest" }
  }
}
```

Inject a named publisher using `[FromKeyedServices]`:

```csharp
public class MyService([FromKeyedServices("broker2")] IRabbitMqPublishService publisher)
{
}
```

Register a named consumer:

```csharp
builder.Services.AddRabbitMqConsumer<PaymentProcessor>("broker2", "payments");
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

`AddRabbitMqConsumer` and `AddRabbitMqRpcConsumer` both call `AddRabbitMqService` internally, so the explicit call is optional when using only consumers.

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

Implement `IRabbitMqRpcMessageProcessingService`. `ReplyAsync` sends the reply **and** acknowledges the message — no separate `AckAsync` call is needed.

```csharp
public class InvoiceProcessor : IRabbitMqRpcMessageProcessingService
{
    public async Task ProcessMessageAsync(RabbitMqRpcMessage message, CancellationToken ct)
    {
        OrderRequest request = message.GetBody<OrderRequest>();

        InvoiceResponse response = new() { /* ... */ };

        await message.ReplyAsync(response, cancellationToken: ct);
    }
}
```

Register with `AddRabbitMqRpcConsumer`:

```csharp
builder.Services.AddRabbitMqRpcConsumer<InvoiceProcessor>("invoices");
```

**Returning an error to the caller:**

Call `ReplyErrorAsync` instead of `ReplyAsync`. It sends the error message back and acknowledges the delivery. The caller receives a `RabbitMqRequestRejectedException`.

```csharp
public async Task ProcessMessageAsync(RabbitMqRpcMessage message, CancellationToken ct)
{
    OrderRequest request = message.GetBody<OrderRequest>();

    if (!IsValid(request))
    {
        await message.ReplyErrorAsync("Invalid request", ct);
        return;
    }

    await message.ReplyAsync(new InvoiceResponse { /* ... */ }, cancellationToken: ct);
}
```

**Fire-and-forget messages on an RPC queue:**

If a message arrives without a `ReplyTo` header (sent fire-and-forget to the same queue), `ReplyAsync` skips the publish and only acknowledges. The handler code does not need to change.

## Health checks

Register a health check that verifies broker connectivity by opening a channel. Requires `AddRabbitMqService()` to have been called first (or `AddRabbitMqConsumer` / `AddRabbitMqRpcConsumer`, which call it internally):

```csharp
builder.Services.AddRabbitMqService();

builder.Services.AddHealthChecks()
    .AddRabbitMq();
```

For a named connection, pass the connection name. The health check name defaults to `rabbitmq:{connectionName}`:

```csharp
builder.Services.AddHealthChecks()
    .AddRabbitMq("broker1")
    .AddRabbitMq("broker2");
```

Override the health check name with the second parameter:

```csharp
builder.Services.AddHealthChecks()
    .AddRabbitMq("broker1", name: "primary-broker");
```

## Error handling

All library errors derive from `RabbitMqException`, so you can catch the base type or a specific subtype:

| Exception | When thrown |
|---|---|
| `RabbitMqUnavailableException` | Broker is unreachable or the channel shuts down unexpectedly |
| `RabbitMqTimeoutException` | `RequestAsync` did not receive a reply within `ReplyTimeoutInSeconds` |
| `RabbitMqRequestRejectedException` | `RequestAsync` received an error reply from the handler via `ReplyErrorAsync` |

```csharp
try
{
    await publisher.PublishAsync("orders", order, cancellationToken: ct);
}
catch (RabbitMqUnavailableException ex)
{
    // broker down — retry, circuit-break, or return 503
}
```

```csharp
try
{
    InvoiceResponse invoice = await publisher.RequestAsync<OrderRequest, InvoiceResponse>(
        "invoices", request, cancellationToken: ct);
}
catch (RabbitMqRequestRejectedException ex)
{
    // handler called ReplyErrorAsync — ex.Message contains the error
}
catch (RabbitMqTimeoutException)
{
    // no reply within ReplyTimeoutInSeconds — return 504
}
catch (RabbitMqUnavailableException)
{
    // broker down — return 503
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
| `ConsumeRpcAsync` | `{queue} receive` | Consumer |

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
| `messaging.publish.messages` | Counter | `{message}` | After each successful `PublishAsync` or `RequestAsync` |
| `messaging.receive.messages` | Counter | `{message}` | On each message delivery |
| `messaging.process.duration` | Histogram | `s` | Time spent in `ProcessMessageAsync` |
| `messaging.client.operation.errors` | Counter | `{message}` | On broker unavailability or request timeout |

All instruments include `messaging.system = "rabbitmq"` and `messaging.destination.name = {queue}` tags. The `messaging.client.operation.errors` counter also includes an `error.type` tag (`broker_unavailable` or `timeout`).

### Log correlation

Each consumed message opens a logger scope with the following properties, available in structured log sinks (Seq, Loki, Application Insights, etc.):

| Key | Value |
|---|---|
| `Queue` | Queue name |
| `MessageId` | AMQP message ID (null if not set by publisher) |
| `DeliveryTag` | Per-channel delivery sequence number |
| `TraceId` | W3C trace ID |
| `SpanId` | W3C span ID |
