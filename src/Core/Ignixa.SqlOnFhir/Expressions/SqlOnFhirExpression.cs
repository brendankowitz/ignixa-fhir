/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Base expression node for SQL on FHIR v2.
 * Provides foundation for ViewDefinition expressions with visitor pattern support.
 */

namespace Ignixa.SqlOnFhir.Expressions;

/// <summary>
/// Base class for all SQL on FHIR expression nodes.
/// Supports visitor pattern for multiple traversal strategies (evaluation, SQL generation, validation).
/// </summary>
public abstract record SqlOnFhirExpression
{
    /// <summary>
    /// Accepts a visitor for traversing the expression tree.
    /// Enables multiple evaluation strategies without modifying node classes.
    /// </summary>
    public abstract TResult Accept<TResult>(ISqlOnFhirExpressionVisitor<TResult> visitor);
}

/// <summary>
/// Visitor interface for traversing SQL on FHIR expression nodes.
/// Implement this to add new behaviors (evaluation, SQL generation, optimization, validation).
/// </summary>
public interface ISqlOnFhirExpressionVisitor<out TResult>
{
    TResult Visit(ViewDefinitionExpression node);
    TResult Visit(SelectExpression node);
    TResult Visit(ColumnExpression node);
    TResult Visit(WhereExpression node);
    TResult Visit(ConstantExpression node);
}
