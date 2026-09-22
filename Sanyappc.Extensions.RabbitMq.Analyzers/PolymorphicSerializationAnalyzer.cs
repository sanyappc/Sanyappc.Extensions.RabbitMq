using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Sanyappc.Extensions.RabbitMq.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PolymorphicSerializationAnalyzer : DiagnosticAnalyzer
{
    private const string HelpLink = "https://github.com/sanyappc/Sanyappc.Extensions.RabbitMq#polymorphic-messages";

    private static readonly DiagnosticDescriptor serializedAsDerived = new(
        id: SerializedAsDerivedId,
        title: "Polymorphic message type passed as derived type",
        messageFormat: "'{0}' is serialized without its type discriminator; pass it as its polymorphic base '{1}'",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "System.Text.Json writes the type discriminator only when serializing through the polymorphic base type. A derived type, alone or inside a collection, is serialized without it, and the consumer cannot tell the derived types apart.",
        helpLinkUri: HelpLink);

    private static readonly DiagnosticDescriptor deserializedAsDerived = new(
        id: DeserializedAsDerivedId,
        title: "Polymorphic message type read as derived type",
        messageFormat: "'{0}' is deserialized without checking its type discriminator; read it as its polymorphic base '{1}' and match on the result",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "System.Text.Json checks the type discriminator only when deserializing through the polymorphic base type. Read as a derived type, the payload of any other derived type is accepted silently, as an instance with default members.",
        helpLinkUri: HelpLink);

    public const string SerializedAsDerivedId = "SANYRMQ001";
    public const string DeserializedAsDerivedId = "SANYRMQ002";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(serializedAsDerived, deserializedAsDerived);

    private static void OnCompilationStart(CompilationStartAnalysisContext context)
    {
        INamedTypeSymbol? jsonPolymorphicAttribute = context.Compilation.GetTypeByMetadataName("System.Text.Json.Serialization.JsonPolymorphicAttribute");
        INamedTypeSymbol? jsonDerivedTypeAttribute = context.Compilation.GetTypeByMetadataName("System.Text.Json.Serialization.JsonDerivedTypeAttribute");
        if (jsonPolymorphicAttribute is null || jsonDerivedTypeAttribute is null)
            return;

        ImmutableArray<INamedTypeSymbol> messageTypes = MessageTypes(context.Compilation);
        if (messageTypes.IsEmpty)
            return;

        PolymorphismAttributes attributes = new(jsonPolymorphicAttribute, jsonDerivedTypeAttribute);
        context.RegisterOperationAction(
            operationContext => AnalyzeInvocation(operationContext, attributes, messageTypes),
            OperationKind.Invocation);
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context, PolymorphismAttributes attributes, ImmutableArray<INamedTypeSymbol> messageTypes)
    {
        IInvocationOperation invocation = (IInvocationOperation)context.Operation;
        IMethodSymbol method = invocation.TargetMethod;

        (int? serialized, int? deserialized) = BodyTypeArguments(method);
        if (serialized is null && deserialized is null)
            return;

        if (!IsMessageType(messageTypes, method.ContainingType))
            return;

        if (serialized is int serializedIndex)
            Report(context, invocation, serializedAsDerived, method.TypeArguments[serializedIndex], BodyArgumentLocation(invocation, serializedIndex), attributes);

        if (deserialized is int deserializedIndex)
            Report(context, invocation, deserializedAsDerived, method.TypeArguments[deserializedIndex], invocation.Syntax.GetLocation(), attributes);
    }

    private static ImmutableArray<INamedTypeSymbol> MessageTypes(Compilation compilation)
    {
        ImmutableArray<INamedTypeSymbol>.Builder messageTypes = ImmutableArray.CreateBuilder<INamedTypeSymbol>(3);
        foreach (string metadataName in new[] { "Sanyappc.Extensions.RabbitMq.IRabbitMqPublishService", "Sanyappc.Extensions.RabbitMq.RabbitMqMessage", "Sanyappc.Extensions.RabbitMq.RabbitMqRpcMessage" })
        {
            if (compilation.GetTypeByMetadataName(metadataName) is { } messageType)
                messageTypes.Add(messageType);
        }

        return messageTypes.ToImmutable();
    }

    private static bool IsMessageType(ImmutableArray<INamedTypeSymbol> messageTypes, INamedTypeSymbol? type)
    {
        foreach (INamedTypeSymbol messageType in messageTypes)
        {
            if (SymbolEqualityComparer.Default.Equals(messageType, type))
                return true;
        }

        return false;
    }

    private static (int? Serialized, int? Deserialized) BodyTypeArguments(IMethodSymbol method) => method.Name switch
    {
        "PublishAsync" or "ReplyAsync" or "SerializeBody" when method.TypeArguments.Length == 1 => (0, null),
        "RequestAsync" when method.TypeArguments.Length == 1 => (0, null),
        "RequestAsync" when method.TypeArguments.Length == 2 => (0, 1),
        "GetBody" or "DeserializeBody" when method.TypeArguments.Length == 1 => (null, 0),
        _ => (null, null),
    };

    private static Location BodyArgumentLocation(IInvocationOperation invocation, int typeArgumentIndex)
    {
        foreach (IArgumentOperation argument in invocation.Arguments)
        {
            if (argument.Parameter is { } parameter
                && parameter.OriginalDefinition.Type is ITypeParameterSymbol typeParameter
                && typeParameter.Ordinal == typeArgumentIndex
                && !argument.IsImplicit)
            {
                return argument.Syntax.GetLocation();
            }
        }

        return invocation.Syntax.GetLocation();
    }

    private static void Report(OperationAnalysisContext context, IInvocationOperation invocation, DiagnosticDescriptor descriptor, ITypeSymbol bodyType, Location location, PolymorphismAttributes attributes)
    {
        (ITypeSymbol Derived, ITypeSymbol Base)? mismatch = FindDerivedOfPolymorphic(bodyType, attributes);
        if (mismatch is null)
            return;

        context.ReportDiagnostic(Diagnostic.Create(descriptor, location, mismatch.Value.Derived.ToDisplayString(), mismatch.Value.Base.ToDisplayString()));
    }

    // A List<Derived> loses the discriminator exactly as a bare Derived does.
    private static (ITypeSymbol Derived, ITypeSymbol Base)? FindDerivedOfPolymorphic(ITypeSymbol type, PolymorphismAttributes attributes)
    {
        if (type is IArrayTypeSymbol array)
            return FindDerivedOfPolymorphic(array.ElementType, attributes);

        ITypeSymbol? polymorphicBase = FindPolymorphicBase(type, attributes);
        if (polymorphicBase is not null)
            return SymbolEqualityComparer.Default.Equals(polymorphicBase, type) ? null : (type, polymorphicBase);

        if (type is INamedTypeSymbol { IsGenericType: true } generic)
        {
            foreach (ITypeSymbol typeArgument in generic.TypeArguments)
            {
                (ITypeSymbol Derived, ITypeSymbol Base)? nested = FindDerivedOfPolymorphic(typeArgument, attributes);
                if (nested is not null)
                    return nested;
            }
        }

        return null;
    }

    // The topmost polymorphic type wins: serializing as a polymorphic middle type still drops the top's discriminator.
    private static ITypeSymbol? FindPolymorphicBase(ITypeSymbol type, PolymorphismAttributes attributes)
    {
        ITypeSymbol? polymorphicBase = null;

        for (ITypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            if (attributes.MarkPolymorphic(current))
                polymorphicBase = current;
        }

        foreach (INamedTypeSymbol implemented in type.AllInterfaces)
        {
            if (attributes.MarkPolymorphic(implemented))
                polymorphicBase = implemented;
        }

        return polymorphicBase;
    }

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(OnCompilationStart);
    }

    // [JsonPolymorphic] is optional in System.Text.Json: [JsonDerivedType] alone makes a type polymorphic.
    private sealed class PolymorphismAttributes(INamedTypeSymbol jsonPolymorphic, INamedTypeSymbol jsonDerivedType)
    {
        public bool MarkPolymorphic(ITypeSymbol type)
        {
            foreach (AttributeData attribute in type.GetAttributes())
            {
                if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, jsonPolymorphic)
                    || SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, jsonDerivedType))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
