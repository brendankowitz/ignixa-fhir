/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * AST builder visitor that converts parse trees into expression ASTs.
 * Default visitor implementation for FhirPath compilation.
 */

using Ignixa.FhirPath.Expressions;
using Ignixa.FhirPath.Parsing.ParseTree;

namespace Ignixa.FhirPath.Parsing;

/// <summary>
/// Converts parse tree nodes into Expression AST nodes.
/// This is the default visitor used during FhirPath compilation.
/// </summary>
internal class AstBuilder : IParseTreeVisitor<AstBuildContext, Expression>
{
    public virtual Expression VisitBinary(BinaryParseNode node, AstBuildContext context)
    {
        var left = node.Left.Accept(this, context);
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
            right = node.Right.Accept(this, context);
        }

        var location = CreateLocation(node.Location);

        return new BinaryExpression(node.Operator, left, right, location);
    }

    public virtual Expression VisitUnary(UnaryParseNode node, AstBuildContext context)
    {
        var operand = node.Operand.Accept(this, context);
        var location = CreateLocation(node.Location);

        return new UnaryExpression(node.Operator, operand, location);
    }

    public virtual Expression VisitFunctionCall(FunctionCallParseNode node, AstBuildContext context)
    {
        var focus = node.Focus?.Accept(this, context);
        var arguments = node.Arguments.Select(arg => arg.Accept(this, context)).ToList();
        var location = CreateLocation(node.Location);

        return new FunctionCallExpression(focus, node.FunctionName, arguments, location);
    }

    public virtual Expression VisitChild(ChildParseNode node, AstBuildContext context)
    {
        var focus = node.Focus.Accept(this, context);
        var location = CreateLocation(node.Location);

        return new ChildExpression(focus, node.ChildName, location);
    }

    public virtual Expression VisitConstant(ConstantParseNode node, AstBuildContext context)
    {
        var location = CreateLocation(node.Location);
        return new ConstantExpression(node.Value, location);
    }

    public virtual Expression VisitIdentifier(IdentifierParseNode node, AstBuildContext context)
    {
        var location = CreateLocation(node.Location);
        return new IdentifierExpression(node.Name, location);
    }

    public virtual Expression VisitPropertyAccess(PropertyAccessParseNode node, AstBuildContext context)
    {
        var focus = node.Focus?.Accept(this, context);
        var location = CreateLocation(node.Location);

        return new PropertyAccessExpression(focus, node.PropertyName, location);
    }

    public virtual Expression VisitVariable(VariableRefParseNode node, AstBuildContext context)
    {
        var location = CreateLocation(node.Location);
        return new VariableRefExpression(node.Name, location);
    }

    public virtual Expression VisitIndexer(IndexerParseNode node, AstBuildContext context)
    {
        var collection = node.Collection.Accept(this, context);
        var index = node.Index.Accept(this, context);
        var location = CreateLocation(node.Location);

        return new IndexerExpression(collection, index, location);
    }

    public virtual Expression VisitParenthesized(ParenthesizedParseNode node, AstBuildContext context)
    {
        var inner = node.InnerExpression.Accept(this, context);
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
