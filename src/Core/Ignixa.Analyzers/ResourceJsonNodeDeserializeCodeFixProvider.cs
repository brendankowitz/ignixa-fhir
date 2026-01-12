using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Ignixa.Analyzers;

/// <summary>
/// Code fix provider that replaces JsonSerializer.Deserialize with ResourceJsonNode.Parse().
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ResourceJsonNodeDeserializeCodeFixProvider)), Shared]
public class ResourceJsonNodeDeserializeCodeFixProvider : CodeFixProvider
{
    public sealed override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(ResourceJsonNodeDeserializeAnalyzer.DiagnosticId);

    public sealed override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root == null)
            return;

        var diagnostic = context.Diagnostics.First();
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        var invocation = root.FindToken(diagnosticSpan.Start).Parent?.AncestorsAndSelf().OfType<InvocationExpressionSyntax>().First();
        if (invocation == null)
            return;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Use ResourceJsonNode.Parse()",
                createChangedDocument: c => ReplaceWithParseAsync(context.Document, invocation, c),
                equivalenceKey: nameof(ResourceJsonNodeDeserializeCodeFixProvider)),
            diagnostic);
    }

    private static async Task<Document> ReplaceWithParseAsync(
        Document document,
        InvocationExpressionSyntax invocation,
        CancellationToken cancellationToken)
    {
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (semanticModel == null)
            return document;

        var symbolInfo = semanticModel.GetSymbolInfo(invocation, cancellationToken);
        if (symbolInfo.Symbol is not IMethodSymbol methodSymbol)
            return document;

        var typeArgument = methodSymbol.TypeArguments[0];
        var typeName = typeArgument.Name;

        // Get the first argument (the JSON string)
        if (invocation.ArgumentList.Arguments.Count == 0)
            return document;

        var jsonArgument = invocation.ArgumentList.Arguments[0];

        // Create new invocation: TypeName.Parse(jsonString)
        var parseInvocation = SyntaxFactory.InvocationExpression(
            SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                SyntaxFactory.IdentifierName(typeName),
                SyntaxFactory.IdentifierName("Parse")),
            SyntaxFactory.ArgumentList(
                SyntaxFactory.SingletonSeparatedList(jsonArgument)))
            .WithLeadingTrivia(invocation.GetLeadingTrivia())
            .WithTrailingTrivia(invocation.GetTrailingTrivia());

        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
            return document;

        var newRoot = root.ReplaceNode(invocation, parseInvocation);
        return document.WithSyntaxRoot(newRoot);
    }
}
