// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Collections.Concurrent;
using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Expressions;
using Ignixa.FhirPath.Parser;

namespace Ignixa.Anonymizer.Extensions;

/// <summary>
/// Extension methods for evaluating FHIRPath expressions on IElement nodes.
/// Uses cached parser/evaluator instances for performance.
/// </summary>
public static class FhirPathExtensions
{
    private static readonly FhirPathParser Parser = new();
    private static readonly FhirPathEvaluator Evaluator = new();
    private static readonly ConcurrentDictionary<string, Expression> ExpressionCache = new();

    /// <summary>
    /// Selects nodes matching a FHIRPath expression.
    /// </summary>
    public static IReadOnlyList<IElement> Select(this IElement element, string expression)
    {
        var compiled = GetOrParseExpression(expression);
        return Evaluator.Evaluate(element, compiled).ToList();
    }

    /// <summary>
    /// Evaluates a FHIRPath predicate expression against a node.
    /// Returns true if the expression evaluates to a truthy value.
    /// </summary>
    public static bool Predicate(this IElement element, string expression)
    {
        var results = element.Select(expression);
        if (results.Count == 0) return false;
        if (results.Count == 1)
        {
            var val = results[0].Value;
            return val switch
            {
                bool b => b,
                string s => !string.IsNullOrEmpty(s),
                null => false,
                _ => true
            };
        }
        return true;
    }

    /// <summary>
    /// Evaluates a FHIRPath expression and returns a single scalar value.
    /// </summary>
    public static object? Scalar(this IElement element, string expression)
    {
        var results = element.Select(expression);
        return results.Count > 0 ? results[0].Value : null;
    }

    private static Expression GetOrParseExpression(string expression)
    {
        return ExpressionCache.GetOrAdd(expression, expr => Parser.Parse(expr));
    }
}
