using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Ignixa.Analyzers;

/// <summary>
/// Analyzer to detect incorrect use of JsonSerializer.Deserialize with ResourceJsonNode types.
/// ResourceJsonNode requires special initialization via Parse() method, not standard JSON deserialization.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class ResourceJsonNodeDeserializeAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "IGNIXA001";
    private const string Category = "Usage";

    private static readonly LocalizableString Title = "Do not use JsonSerializer.Deserialize with ResourceJsonNode";
    private static readonly LocalizableString MessageFormat = "Use '{0}.Parse()' instead of 'JsonSerializer.Deserialize<{0}>()'";
    private static readonly LocalizableString Description =
        "ResourceJsonNode and its derived types require special initialization that sets up internal navigation structures. " +
        "Use the Parse() static method instead of JsonSerializer.Deserialize(), which creates an uninitialized object with no children.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Error,
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

        // Check if this is a member access (e.g., JsonSerializer.Deserialize)
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return;

        // Check if the method name is "Deserialize"
        if (memberAccess.Name.Identifier.ValueText != "Deserialize")
            return;

        // Get the symbol for the invocation
        var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken);
        if (symbolInfo.Symbol is not IMethodSymbol methodSymbol)
            return;

        // Check if this is JsonSerializer.Deserialize
        if (methodSymbol.ContainingType?.ToString() != "System.Text.Json.JsonSerializer")
            return;

        // Check if it's a generic method
        if (!methodSymbol.IsGenericMethod || methodSymbol.TypeArguments.Length != 1)
            return;

        var typeArgument = methodSymbol.TypeArguments[0];

        // Check if the type argument is or derives from ResourceJsonNode
        if (!IsResourceJsonNodeOrDerived(typeArgument))
            return;

        // Report diagnostic
        var diagnostic = Diagnostic.Create(
            Rule,
            invocation.GetLocation(),
            typeArgument.Name);

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
        // This catches derived types like PatientJsonNode, ObservationJsonNode, etc.
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
