/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * AST builder visitor that converts parse trees into expression ASTs.
 * Default visitor implementation for FhirPath compilation.
 */

using System.Runtime.CompilerServices;
using Ignixa.FhirPath.Expressions;
using Ignixa.FhirPath.Parsing.ParseTree;

namespace Ignixa.FhirPath.Parsing;

/// <summary>
/// Converts parse tree nodes into Expression AST nodes.
/// This is the default visitor used during FhirPath compilation.
/// </summary>
internal class AstBuilder : IParseTreeVisitor<AstBuildContext, Expression>
{
    /// <summary>
    /// Builds a child node. Every recursive step goes through here so the stack check covers it.
    /// </summary>
    /// <remarks>
    /// The parser bounds tree depth before building, so the check only fires on a thread whose stack is
    /// smaller than that bound assumes; it turns what would be a fatal overflow into a catchable exception.
    /// </remarks>
    protected Expression Build(ParseNode node, AstBuildContext context)
    {
        RuntimeHelpers.EnsureSufficientExecutionStack();
        return node.Accept(this, context);
    }

    public virtual Expression VisitBinary(BinaryParseNode node, AstBuildContext context)
    {
        var left = Build(node.Left, context);
        Expression right;

        // For 'is' and 'as' binary operators, the right operand is a type specifier.
        // Convert IdentifierParseNode to ConstantExpression with the type name as a string value.
        // This allows the analyzer to handle type checking correctly.
        if ((node.Operator == "is" || node.Operator == "as") && node.Right is IdentifierParseNode idNode)
        {
            right = new ConstantExpression(idNode.Name, CreateLocation(idNode.Location));
        }
        else
        {
            right = Build(node.Right, context);
        }

        var location = CreateLocation(node.Location);

        return new BinaryExpression(node.Operator, left, right, location);
    }

    public virtual Expression VisitUnary(UnaryParseNode node, AstBuildContext context)
    {
        var operand = Build(node.Operand, context);
        var location = CreateLocation(node.Location);

        return new UnaryExpression(node.Operator, operand, location);
    }

    public virtual Expression VisitFunctionCall(FunctionCallParseNode node, AstBuildContext context)
    {
        var focus = node.Focus is null ? null : Build(node.Focus, context);
        var arguments = node.Arguments.Select(arg => Build(arg, context)).ToList();
        var location = CreateLocation(node.Location);

        return new FunctionCallExpression(focus, node.FunctionName, arguments, location);
    }

    public virtual Expression VisitChild(ChildParseNode node, AstBuildContext context)
    {
        var focus = Build(node.Focus, context);
        var location = CreateLocation(node.Location);

        return new ChildExpression(focus, node.ChildName, location);
    }

    public virtual Expression VisitConstant(ConstantParseNode node, AstBuildContext context)
    {
        var location = CreateLocation(node.Location);
        return node is TemporalConstantParseNode { Value: string literal }
            ? new TemporalConstantExpression(literal, location)
            : new ConstantExpression(node.Value, location);
    }

    public virtual Expression VisitIdentifier(IdentifierParseNode node, AstBuildContext context)
    {
        var location = CreateLocation(node.Location);
        return new IdentifierExpression(node.Name, location);
    }

    public virtual Expression VisitPropertyAccess(PropertyAccessParseNode node, AstBuildContext context)
    {
        var focus = node.Focus is null ? null : Build(node.Focus, context);
        var location = CreateLocation(node.Location);

        return new PropertyAccessExpression(focus, node.PropertyName, location);
    }

    public virtual Expression VisitVariable(VariableRefParseNode node, AstBuildContext context)
    {
        var location = CreateLocation(node.Location);
        return new VariableRefExpression(node.Name, location, node.IsDelimited);
    }

    public virtual Expression VisitIndexer(IndexerParseNode node, AstBuildContext context)
    {
        var collection = Build(node.Collection, context);
        var index = Build(node.Index, context);
        var location = CreateLocation(node.Location);

        return new IndexerExpression(collection, index, location);
    }

    public virtual Expression VisitParenthesized(ParenthesizedParseNode node, AstBuildContext context)
    {
        var inner = Build(node.InnerExpression, context);
        var location = CreateLocation(node.Location);

        return new ParenthesizedExpression(inner, location);
    }

    public virtual Expression VisitQuantity(QuantityParseNode node, AstBuildContext context)
    {
        var location = CreateLocation(node.Location);
        return new QuantityExpression(node.Value, node.Unit, location);
    }

    public virtual Expression VisitScope(ScopeParseNode node, AstBuildContext context)
    {
        var location = CreateLocation(node.Location);
        return new ScopeExpression(node.ScopeName, location);
    }

    public virtual Expression VisitEmpty(EmptyParseNode node, AstBuildContext context)
    {
        var location = CreateLocation(node.Location);
        return new EmptyExpression(location);
    }

    public virtual Expression VisitInstanceSelector(InstanceSelectorParseNode node, AstBuildContext context)
    {
        var elementAssignments = node.Elements
            .Select(e => new ElementAssignment(
                e.ElementName,
                Build(e.ValueExpression, context)))
            .ToList();

        return new InstanceSelectorExpression(
            node.TypeName,
            elementAssignments,
            node.NamespacePrefix,
            node.IsEmpty,
            CreateLocation(node.Location));
    }

    protected static ISourcePositionInfo CreateLocation(SourceLocation location) =>
        new FhirPathExpressionLocationInfo
        {
            LineNumber = location.Line,
            LinePosition = location.Column,
            RawPosition = location.Position,
            Length = location.Length
        };
}

/// <summary>
/// Context passed during AST building.
/// Can be extended to carry state through the compilation process.
/// </summary>
internal record AstBuildContext
{
    public bool PreserveTrivia { get; init; }
    public string? SourceExpression { get; init; }

    public static readonly AstBuildContext Default = new();
}
