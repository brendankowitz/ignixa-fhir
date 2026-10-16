// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.SqlOnFhir.Evaluation;
using Ignixa.SqlOnFhir.Expressions;

#pragma warning disable CS0618 // ISourceNavigator is what the SQL-on-FHIR evaluator takes; its migration is pending upstream.

namespace Ignixa.CliOnFhir.Views;

/// <summary>A validated ViewDefinition, ready to evaluate.</summary>
/// <param name="Navigator">The ViewDefinition (with any extra <c>where</c> added) as the evaluator takes it.</param>
/// <param name="Expression">The parsed view.</param>
/// <param name="Columns">The output columns, in the order a row's cells follow.</param>
internal sealed record CompiledView(ISourceNavigator Navigator, ViewDefinitionExpression Expression, IReadOnlyList<ColumnSchema> Columns)
{
    /// <summary>The resource type the view runs over.</summary>
    public string Resource => Expression.Resource;

    /// <summary>
    /// Whether the evaluator orders this view's rows across resources, not resource by resource: a top-level <c>unionAll</c>
    /// is evaluated branch by branch over every resource (SQL UNION ALL), so a row then has no single source resource. This is
    /// a superset of the evaluator's own test, which only makes evaluating all resources in one batch (the same rows) more
    /// likely, never row provenance wrong.
    /// </summary>
    public bool OrdersAcrossResources => Expression.Select.Any(select => !select.UnionAll.IsEmpty && select.ForEach is null && select.ForEachOrNull is null);
}
