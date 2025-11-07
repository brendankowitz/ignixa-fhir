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
    bool Collection) : SqlOnFhirExpression
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
public sealed record ConstantExpression(
    string Name,
    object? Value) : SqlOnFhirExpression
{
    public override TResult Accept<TResult>(ISqlOnFhirExpressionVisitor<TResult> visitor)
        => visitor.Visit(this);
}
