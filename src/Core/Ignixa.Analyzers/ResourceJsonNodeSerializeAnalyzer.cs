using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Ignixa.Analyzers;

/// <summary>
/// Analyzer to detect incorrect use of JsonSerializer.Serialize with ResourceJsonNode types.
/// ResourceJsonNode already handles serialization through its internal JsonObject representation.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class ResourceJsonNodeSerializeAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "IGNIXA002";
    private const string Category = "Usage";

    private static readonly LocalizableString Title = "Do not use JsonSerializer.Serialize with ResourceJsonNode";
    private static readonly LocalizableString MessageFormat = "Use '{0}.ToJson()' or convert to string directly instead of 'JsonSerializer.Serialize()'";
    private static readonly LocalizableString Description =
        "ResourceJsonNode types are already JSON-backed and don't require JsonSerializer.Serialize(). " +
        "Use the ToJson() method or ToString() to get the JSON representation, or access the MutableNode property directly.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: Description,
        helpLinkUri: "https://brendankowitz.github.io/ignixa-fhir/docs/serialization");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        // Check if this is a member access (e.g., JsonSerializer.Serialize)
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return;

        // Check if the method name is "Serialize"
        if (memberAccess.Name.Identifier.ValueText != "Serialize")
            return;

        // Get the symbol for the invocation
        var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken);
        if (symbolInfo.Symbol is not IMethodSymbol methodSymbol)
            return;

        // Check if this is JsonSerializer.Serialize
        if (methodSymbol.ContainingType?.ToString() != "System.Text.Json.JsonSerializer")
            return;

        // Get the first argument (the object being serialized)
        if (invocation.ArgumentList.Arguments.Count == 0)
            return;

        var firstArgument = invocation.ArgumentList.Arguments[0].Expression;
        var typeInfo = context.SemanticModel.GetTypeInfo(firstArgument, context.CancellationToken);

        if (typeInfo.Type == null)
            return;

        // Check if the type is or derives from ResourceJsonNode
        if (!IsResourceJsonNodeOrDerived(typeInfo.Type))
            return;

        // Report diagnostic
        var diagnostic = Diagnostic.Create(
            Rule,
            invocation.GetLocation(),
            typeInfo.Type.Name);

        context.ReportDiagnostic(diagnostic);
    }

    private static bool IsResourceJsonNodeOrDerived(ITypeSymbol typeSymbol)
    {
        if (typeSymbol == null)
            return false;

        // Check current type
        if (typeSymbol.ToString() == "Ignixa.Serialization.SourceNodes.ResourceJsonNode")
            return true;

        // Check if it's in the Ignixa.Serialization namespace and ends with "JsonNode"
        if (typeSymbol.ContainingNamespace?.ToString()?.StartsWith("Ignixa.Serialization", System.StringComparison.Ordinal) == true &&
            typeSymbol.Name.EndsWith("JsonNode", System.StringComparison.Ordinal))
        {
            // Walk up the inheritance chain
            var current = typeSymbol.BaseType;
            while (current != null)
            {
                if (current.ToString() == "Ignixa.Serialization.SourceNodes.ResourceJsonNode")
                    return true;
                current = current.BaseType;
            }
        }

        return false;
    }
}
