/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Parse tree nodes for FhirPath expressions.
 * Separates syntactic parsing from AST construction via visitor pattern.
 */

using Superpower.Model;
using Ignixa.FhirPath.Parser;

namespace Ignixa.FhirPath.Parsing.ParseTree;

/// <summary>
/// Source location information for parse tree nodes.
/// </summary>
/// <param name="Line">1-based line number</param>
/// <param name="Column">1-based column number</param>
/// <param name="Position">0-based absolute position</param>
/// <param name="Length">Length in characters</param>
internal readonly record struct SourceLocation(int Line, int Column, int Position, int Length)
{
    public static SourceLocation From(Token<FhirPathTokenKind> token) =>
        new(token.Position.Line, token.Position.Column, (int)token.Position.Absolute, token.Span.Length);

    public static SourceLocation From(Token<FhirPathTokenKind> start, Token<FhirPathTokenKind> end) =>
        new(
            start.Position.Line,
            start.Position.Column,
            (int)start.Position.Absolute,
            (int)(end.Position.Absolute - start.Position.Absolute) + end.Span.Length);
}

/// <summary>
/// Base class for all parse tree nodes.
/// Parse trees represent syntactic structure before semantic analysis.
/// </summary>
internal abstract record ParseNode(SourceLocation Location)
{
    public abstract TResult Accept<TContext, TResult>(IParseTreeVisitor<TContext, TResult> visitor, TContext context);
}

/// <summary>
/// Represents a binary operation in the parse tree.
/// Examples: age > 18, 1 + 2, name = 'John'
/// </summary>
internal sealed record BinaryParseNode(
    ParseNode Left,
    string Operator,
    ParseNode Right,
    SourceLocation Location) : ParseNode(Location)
{
    public override TResult Accept<TContext, TResult>(IParseTreeVisitor<TContext, TResult> visitor, TContext context)
        => visitor.VisitBinary(this, context);
}

/// <summary>
/// Represents a unary operation in the parse tree.
/// Examples: -5, +10
/// </summary>
internal sealed record UnaryParseNode(
    string Operator,
    ParseNode Operand,
    SourceLocation Location) : ParseNode(Location)
{
    public override TResult Accept<TContext, TResult>(IParseTreeVisitor<TContext, TResult> visitor, TContext context)
        => visitor.VisitUnary(this, context);
}

/// <summary>
/// Represents a function call in the parse tree.
/// Examples: exists(), where($this > 5), first()
/// </summary>
internal sealed record FunctionCallParseNode(
    ParseNode? Focus,
    string FunctionName,
    IReadOnlyList<ParseNode> Arguments,
    SourceLocation Location) : ParseNode(Location)
{
    public override TResult Accept<TContext, TResult>(IParseTreeVisitor<TContext, TResult> visitor, TContext context)
        => visitor.VisitFunctionCall(this, context);
}

/// <summary>
/// Represents child/member access in the parse tree (dot notation).
/// Examples: Patient.name, name.given
/// </summary>
internal sealed record ChildParseNode(
    ParseNode Focus,
    string ChildName,
    SourceLocation Location) : ParseNode(Location)
{
    public override TResult Accept<TContext, TResult>(IParseTreeVisitor<TContext, TResult> visitor, TContext context)
        => visitor.VisitChild(this, context);
}

/// <summary>
/// Represents a constant value in the parse tree.
/// Examples: 42, 3.14, 'hello', true, @2024-01-15
/// </summary>
internal sealed record ConstantParseNode(
    object Value,
    SourceLocation Location) : ParseNode(Location)
{
    public override TResult Accept<TContext, TResult>(IParseTreeVisitor<TContext, TResult> visitor, TContext context)
        => visitor.VisitConstant(this, context);
}

/// <summary>
/// Represents an identifier (type specifier) in the parse tree.
/// Used primarily for ofType() and as() arguments.
/// Examples: Patient, string, Quantity
/// </summary>
internal sealed record IdentifierParseNode(
    string Name,
    SourceLocation Location) : ParseNode(Location)
{
    public override TResult Accept<TContext, TResult>(IParseTreeVisitor<TContext, TResult> visitor, TContext context)
        => visitor.VisitIdentifier(this, context);
}

/// <summary>
/// Represents property access at root level in the parse tree.
/// Eliminates ambiguity between property access and function calls.
/// Examples: name, family (when used without parentheses)
/// </summary>
internal sealed record PropertyAccessParseNode(
    ParseNode? Focus,
    string PropertyName,
    SourceLocation Location) : ParseNode(Location)
{
    public override TResult Accept<TContext, TResult>(IParseTreeVisitor<TContext, TResult> visitor, TContext context)
        => visitor.VisitPropertyAccess(this, context);
}

/// <summary>
/// Represents a variable reference in the parse tree.
/// Examples: %context, %resource, %ext-id
/// </summary>
internal sealed record VariableRefParseNode(
    string Name,
    SourceLocation Location) : ParseNode(Location)
{
    public override TResult Accept<TContext, TResult>(IParseTreeVisitor<TContext, TResult> visitor, TContext context)
        => visitor.VisitVariable(this, context);
}

/// <summary>
/// Represents indexer access in the parse tree.
/// Examples: name[0], collection[5]
/// </summary>
internal sealed record IndexerParseNode(
    ParseNode Collection,
    ParseNode Index,
    SourceLocation Location) : ParseNode(Location)
{
    public override TResult Accept<TContext, TResult>(IParseTreeVisitor<TContext, TResult> visitor, TContext context)
        => visitor.VisitIndexer(this, context);
}

/// <summary>
/// Represents a parenthesized expression in the parse tree.
/// Examples: (1 + 2), (name.exists())
/// </summary>
internal sealed record ParenthesizedParseNode(
    ParseNode InnerExpression,
    SourceLocation Location) : ParseNode(Location)
{
    public override TResult Accept<TContext, TResult>(IParseTreeVisitor<TContext, TResult> visitor, TContext context)
        => visitor.VisitParenthesized(this, context);
}

/// <summary>
/// Represents a quantity literal in the parse tree.
/// Examples: 5 'mg', 37.5 'Cel', 100 '[lb_av]'
/// </summary>
internal sealed record QuantityParseNode(
    decimal Value,
    string Unit,
    SourceLocation Location) : ParseNode(Location)
{
    public override TResult Accept<TContext, TResult>(IParseTreeVisitor<TContext, TResult> visitor, TContext context)
        => visitor.VisitQuantity(this, context);
}

/// <summary>
/// Represents a scope reference in the parse tree.
/// Examples: $this, $index, $total
/// </summary>
internal sealed record ScopeParseNode(
    string ScopeName,
    SourceLocation Location) : ParseNode(Location)
{
    public override TResult Accept<TContext, TResult>(IParseTreeVisitor<TContext, TResult> visitor, TContext context)
        => visitor.VisitScope(this, context);
}

/// <summary>
/// Represents an empty collection literal in the parse tree.
/// Example: {}
/// </summary>
internal sealed record EmptyParseNode(
    SourceLocation Location) : ParseNode(Location)
{
    public override TResult Accept<TContext, TResult>(IParseTreeVisitor<TContext, TResult> visitor, TContext context)
        => visitor.VisitEmpty(this, context);
}

/// <summary>
/// Represents an element assignment in an instance selector.
/// Example: system: 'http://example.org'
/// </summary>
internal sealed record ElementAssignmentParseNode(
    string ElementName,
    ParseNode ValueExpression,
    SourceLocation Location) : ParseNode(Location)
{
    // This is a helper node, doesn't need Accept method as it's not visited directly
    public override TResult Accept<TContext, TResult>(IParseTreeVisitor<TContext, TResult> visitor, TContext context)
        => throw new NotSupportedException("ElementAssignmentParseNode is not directly visitable");
}

/// <summary>
/// Represents an instance selector expression in the parse tree.
/// Examples:
/// - Coding { system: 'http://example.org', code: 'c1' }
/// - FHIR.Identifier { system: 'http://example.org', value: 'N0001' }
/// - Period {:}
/// </summary>
internal sealed record InstanceSelectorParseNode(
    string TypeName,
    string? NamespacePrefix,
    IReadOnlyList<ElementAssignmentParseNode> Elements,
    bool IsEmpty,
    SourceLocation Location) : ParseNode(Location)
{
    public override TResult Accept<TContext, TResult>(IParseTreeVisitor<TContext, TResult> visitor, TContext context)
        => visitor.VisitInstanceSelector(this, context);
}
