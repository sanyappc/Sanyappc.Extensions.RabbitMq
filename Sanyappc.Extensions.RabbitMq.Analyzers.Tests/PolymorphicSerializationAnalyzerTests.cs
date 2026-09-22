using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;

namespace Sanyappc.Extensions.RabbitMq.Analyzers.Tests;

public sealed class PolymorphicSerializationAnalyzerTests
{
    // The analyzer matches the library's surface by metadata name, so a stub with the same shape stands in for it.
    private const string Library = """
        namespace Sanyappc.Extensions.RabbitMq
        {
            using System.Text.Json;
            using System.Threading;
            using System.Threading.Tasks;

            public interface IRabbitMqPublishService
            {
                Task PublishAsync<T>(string queue, T body, JsonSerializerOptions? options = null, CancellationToken cancellationToken = default);
                Task RequestAsync<TIn>(string queue, TIn body, JsonSerializerOptions? options = null, CancellationToken cancellationToken = default);
                Task<TOut> RequestAsync<TIn, TOut>(string queue, TIn body, JsonSerializerOptions? options = null, CancellationToken cancellationToken = default);
            }

            public class RabbitMqMessage
            {
                public T GetBody<T>(JsonSerializerOptions? options = null) => default!;
                public static T DeserializeBody<T>(System.ReadOnlySpan<byte> message, JsonSerializerOptions? options = null) => default!;
                public static byte[] SerializeBody<T>(T message, JsonSerializerOptions? options = null) => default!;
            }

            public class RabbitMqRpcMessage
            {
                public T GetBody<T>(JsonSerializerOptions? options = null) => default!;
                public Task ReplyAsync<T>(T body, JsonSerializerOptions? options = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
            }
        }

        namespace Elsewhere
        {
            public class Publisher
            {
                public System.Threading.Tasks.Task PublishAsync<T>(string queue, T body) => System.Threading.Tasks.Task.CompletedTask;
            }
        }

        """;

    private const string Messages = """
        [JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
        [JsonDerivedType(typeof(Delete), "delete")]
        [JsonDerivedType(typeof(Block), "block")]
        public abstract record Deactivate
        {
            public sealed record Delete : Deactivate { }
            public sealed record Block : Deactivate { }
        }

        [JsonDerivedType(typeof(Paid), "paid")]
        public abstract record Invoice
        {
            public sealed record Paid : Invoice { }
        }

        [JsonPolymorphic]
        [JsonDerivedType(typeof(Started), "started")]
        public interface IEvent { }

        public sealed record Started : IEvent { }

        [JsonDerivedType(typeof(Middle), "middle")]
        public abstract record Top { }

        [JsonDerivedType(typeof(Leaf), "leaf")]
        public abstract record Middle : Top { }

        public sealed record Leaf : Middle { }

        """;

    private const string Usings = """
        using System.Collections.Generic;
        using System.Text.Json.Serialization;
        using System.Threading.Tasks;
        using Sanyappc.Extensions.RabbitMq;

        """;

    private static Task VerifyAsync(string usage, params DiagnosticResult[] expected)
    {
        CSharpAnalyzerTest<PolymorphicSerializationAnalyzer, DefaultVerifier> test = new()
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = Usings + Library + Messages + usage,
        };
        test.ExpectedDiagnostics.AddRange(expected);

        return test.RunAsync(TestContext.Current.CancellationToken);
    }

    private static DiagnosticResult SerializedAsDerived(int marker, string derived, string polymorphicBase) =>
        new DiagnosticResult(PolymorphicSerializationAnalyzer.SerializedAsDerivedId, DiagnosticSeverity.Warning)
            .WithLocation(marker)
            .WithArguments(derived, polymorphicBase);

    private static DiagnosticResult DeserializedAsDerived(int marker, string derived, string polymorphicBase) =>
        new DiagnosticResult(PolymorphicSerializationAnalyzer.DeserializedAsDerivedId, DiagnosticSeverity.Warning)
            .WithLocation(marker)
            .WithArguments(derived, polymorphicBase);

    [Fact]
    public Task ADerivedTypeIsReportedOnTheBodyArgument() => VerifyAsync("""
        public static class Usage
        {
            public static Task Send(IRabbitMqPublishService publisher, Deactivate.Delete message) =>
                publisher.PublishAsync("q", {|#0:message|});
        }
        """, SerializedAsDerived(0, "Deactivate.Delete", "Deactivate"));

    [Fact]
    public Task TheBaseTypeIsClean() => VerifyAsync("""
        public static class Usage
        {
            public static Task Send(IRabbitMqPublishService publisher, Deactivate message) =>
                publisher.PublishAsync("q", message);
        }
        """);

    [Fact]
    public Task AnExplicitBaseTypeArgumentIsClean() => VerifyAsync("""
        public static class Usage
        {
            public static Task Send(IRabbitMqPublishService publisher, Deactivate.Delete message) =>
                publisher.PublishAsync<Deactivate>("q", message);
        }
        """);

    [Fact]
    public Task ABaseMarkedOnlyWithJsonDerivedTypeIsPolymorphicToo() => VerifyAsync("""
        public static class Usage
        {
            public static Task Send(IRabbitMqPublishService publisher, Invoice.Paid message) =>
                publisher.RequestAsync("q", {|#0:message|});
        }
        """, SerializedAsDerived(0, "Invoice.Paid", "Invoice"));

    [Fact]
    public Task APolymorphicInterfaceCountsAsTheBase() => VerifyAsync("""
        public static class Usage
        {
            public static Task Send(RabbitMqRpcMessage message, Started reply) =>
                message.ReplyAsync({|#0:reply|});
        }
        """, SerializedAsDerived(0, "Started", "IEvent"));

    [Fact]
    public Task TheTopmostPolymorphicTypeIsTheOneToPassAs() => VerifyAsync("""
        public static class Usage
        {
            public static Task Send(IRabbitMqPublishService publisher, Middle message) =>
                publisher.PublishAsync("q", {|#0:message|});
        }
        """, SerializedAsDerived(0, "Middle", "Top"));

    [Fact]
    public Task ACollectionOfDerivedTypesLosesTheDiscriminatorToo() => VerifyAsync("""
        public static class Usage
        {
            public static Task Send(IRabbitMqPublishService publisher, List<Deactivate.Delete> batch) =>
                publisher.PublishAsync("q", {|#0:batch|});

            public static byte[] Bytes(Deactivate.Block[] batch) =>
                RabbitMqMessage.SerializeBody({|#1:batch|});
        }
        """,
        SerializedAsDerived(0, "Deactivate.Delete", "Deactivate"),
        SerializedAsDerived(1, "Deactivate.Block", "Deactivate"));

    [Fact]
    public Task ACollectionOfTheBaseTypeIsClean() => VerifyAsync("""
        public static class Usage
        {
            public static Task Send(IRabbitMqPublishService publisher, List<Deactivate> batch) =>
                publisher.PublishAsync("q", batch);
        }
        """);

    [Fact]
    public Task ReadingADerivedTypeIsReported() => VerifyAsync("""
        public static class Usage
        {
            public static Deactivate.Delete Read(RabbitMqMessage message) =>
                {|#0:message.GetBody<Deactivate.Delete>()|};

            public static Deactivate ReadAsBase(RabbitMqRpcMessage message) =>
                message.GetBody<Deactivate>();
        }
        """, DeserializedAsDerived(0, "Deactivate.Delete", "Deactivate"));

    [Fact]
    public Task ARequestChecksItsBodyAndItsReplySeparately() => VerifyAsync("""
        public static class Usage
        {
            public static Task<Deactivate> Ask(IRabbitMqPublishService publisher, Deactivate.Delete request) =>
                publisher.RequestAsync<Deactivate.Delete, Deactivate>("q", {|#0:request|});

            public static Task<Deactivate.Block> Answer(IRabbitMqPublishService publisher, Deactivate request) =>
                {|#1:publisher.RequestAsync<Deactivate, Deactivate.Block>("q", request)|};
        }
        """,
        SerializedAsDerived(0, "Deactivate.Delete", "Deactivate"),
        DeserializedAsDerived(1, "Deactivate.Block", "Deactivate"));

    [Fact]
    public Task AMethodOfTheSameNameElsewhereIsClean() => VerifyAsync("""
        public static class Usage
        {
            public static Task Send(Elsewhere.Publisher publisher, Deactivate.Delete message) =>
                publisher.PublishAsync("q", message);
        }
        """);
}
