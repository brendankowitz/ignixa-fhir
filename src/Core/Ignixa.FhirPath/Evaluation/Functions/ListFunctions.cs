// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.FhirPath.Attributes;
using Ignixa.FhirPath.Expressions;

namespace Ignixa.FhirPath.Evaluation.Functions;

/// <summary>
/// list() function implementation for FhirPath expressions.
/// </summary>
internal static class ListFunctions
{
    /// <summary>
    /// list(item: collection, ...) - Appends the items of each argument into a single collection,
    /// retaining order and duplicates. The input collection is ignored; fluent invocation is rejected
    /// by the evaluator and analyzer.
    /// </summary>
    [FhirPathFunction("list",
        SupportedContexts = "any-any",
        ReturnType = "any",
        SupportsCollections = true,
        SupportedAtRoot = true,
        MinArguments = 0,
        MaxArguments = int.MaxValue,
        Category = "Collection",
        Description = "Appends the items from each argument into a single collection, preserving order and duplicates")]
    public static IEnumerable<IElement> List(
        IEnumerable<IElement> focus,
        IReadOnlyList<Expression> arguments,
        EvaluationContext context,
        Func<IEnumerable<IElement>, Expression, EvaluationContext, IEnumerable<IElement>> evaluateExpression)
    {
        var result = new List<IElement>();

        // Non-scoped function: evaluate arguments in the outer context (don't change $this)
        foreach (var arg in arguments)
        {
            result.AddRange(evaluateExpression(context.Focus, arg, context));
        }

        return result;
    }
}
