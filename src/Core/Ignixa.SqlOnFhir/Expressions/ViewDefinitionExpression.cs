/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Expression nodes representing a complete ViewDefinition.
 * Immutable records for thread-safety and functional composition.
 * Stores compiled FHIRPath Expression objects for performance.
 */

using System.Collections.Immutable;
using Ignixa.FhirPath.Expressions;

namespace Ignixa.SqlOnFhir.Expressions;

/// <summary>
/// Expression representing a SQL on FHIR v2 ViewDefinition.
/// Maps FHIR resources to tabular rows using compiled FHIRPath expressions.
/// </summary>
public sealed record ViewDefinitionExpression(
    string Resource,
    string? Status,
    ImmutableArray<ConstantExpression> Constants,
    ImmutableArray<WhereExpression> Where,
    ImmutableArray<SelectExpression> Select) : SqlOnFhirExpression
{
    public override TResult Accept<TResult>(ISqlOnFhirExpressionVisitor<TResult> visitor)
        => visitor.Visit(this);
}

/// <summary>
/// Expression representing a SELECT group with optional forEach unnesting or repeat traversal.
/// </summary>
public sealed record SelectExpression(
    Expression? ForEach,
    Expression? ForEachOrNull,
    ImmutableArray<Expression> Repeat,
    ImmutableArray<ColumnExpression> Columns,
    ImmutableArray<SelectExpression> NestedSelect,
    ImmutableArray<SelectExpression> UnionAll) : SqlOnFhirExpression
{
    public override TResult Accept<TResult>(ISqlOnFhirExpressionVisitor<TResult> visitor)
        => visitor.Visit(this);
}

/// <summary>
/// Expression representing a column definition with compiled FHIRPath expression.
/// </summary>
public sealed record ColumnExpression(
    string Name,
    Expression Path,
    string? Type,
    bool Collection,
    ImmutableArray<(string Name, string Value)> Tags = default) : SqlOnFhirExpression
{
    public override TResult Accept<TResult>(ISqlOnFhirExpressionVisitor<TResult> visitor)
        => visitor.Visit(this);
}

/// <summary>
/// Expression representing a WHERE clause filter with compiled FHIRPath.
/// </summary>
public sealed record WhereExpression(
    Expression Filter) : SqlOnFhirExpression
{
    public override TResult Accept<TResult>(ISqlOnFhirExpressionVisitor<TResult> visitor)
        => visitor.Visit(this);
}

/// <summary>
/// Expression representing a constant value that can be referenced as %name.
/// </summary>
/// <param name="Name">The constant's name, referenced as <c>%Name</c>.</param>
/// <param name="Value">The constant's value, in whatever CLR type carries it.</param>
/// <param name="ValueType">
/// The FHIRPath type the declared <c>value[x]</c> suffix converts to, or <see langword="null"/> when the
/// suffix was absent or unrecognised.
/// </param>
/// <remarks>
/// <see cref="ValueType"/> exists because the CLR type of <see cref="Value"/> cannot always recover it.
/// <c>valueDate</c>, <c>valueDateTime</c>, <c>valueInstant</c> and <c>valueTime</c> all arrive as a
/// <see cref="string"/>, so a constant bound without this typed as System.String and
/// <c>birthDate &lt; %cutoff</c> - a conformant ViewDefinition - failed as a comparison between a date
/// and a string once the comparison operators started rejecting operand types FHIRPath does not relate.
/// </remarks>
public sealed record ConstantExpression(
    string Name,
    object? Value,
    string? ValueType = null) : SqlOnFhirExpression
{
    public override TResult Accept<TResult>(ISqlOnFhirExpressionVisitor<TResult> visitor)
        => visitor.Visit(this);
}
