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
    /// Whether the evaluator orders this view's rows across resources, not resource by resource: a top-level select that is only a
    /// <c>unionAll</c> of plain branches is evaluated branch by branch over every resource (SQL UNION ALL), so a row then has no
    /// single source resource. Mirrors the evaluator's own test (<c>SqlOnFhirEvaluationVisitor.IsBatchOrderedUnionAllSelect</c>)
    /// condition for condition; a view with columns beside the <c>unionAll</c>, or branches that unnest, is evaluated per resource.
    /// </summary>
    public bool OrdersAcrossResources => Expression.Select.Any(IsBatchOrderedUnionAll);

    private static bool IsBatchOrderedUnionAll(SelectExpression select) =>
        !select.UnionAll.IsEmpty
        && select.ForEach is null
        && select.ForEachOrNull is null
        && select.Repeat.IsEmpty
        && select.Columns.IsEmpty
        && select.NestedSelect.IsEmpty
        && select.UnionAll.All(branch => branch.ForEach is null && branch.ForEachOrNull is null && branch.Repeat.IsEmpty);
}
