// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;

namespace Ignixa.DeId.Extensions;

/// <summary>
/// Extension methods for evaluating FHIRPath expressions on IElement nodes.
/// Delegates to the FHIRPath library, whose expression caches are size-bounded. Rule paths come from
/// request-supplied configuration, so a cache owned here would grow with attacker-chosen text.
/// </summary>
public static class FhirPathExtensions
{
    /// <summary>
    /// Selects nodes matching a FHIRPath expression. Results are materialized here so that a
    /// failure during lazy evaluation surfaces from this call rather than from a later enumeration.
    /// </summary>
    /// <param name="element">The node to evaluate against.</param>
    /// <param name="expression">The FHIRPath expression.</param>
    /// <param name="cancellationToken">Stops the evaluation, which also runs under the default evaluation limits.</param>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <exception cref="Exception">The expression cannot be parsed or evaluated against this element. The
    /// library reports these with several exception types, so callers treat any failure as an unevaluable expression.</exception>
    public static IReadOnlyList<IElement> Select(this IElement element, string expression, CancellationToken cancellationToken) =>
        element.Select(expression, requestSupplied: true, cancellationToken);

    /// <summary>
    /// Selects nodes matching a FHIRPath expression, evaluated in the request-supplied expression tier or, for an
    /// expression the server itself defines, the server tier.
    /// </summary>
    /// <param name="element">The node to evaluate against.</param>
    /// <param name="expression">The FHIRPath expression.</param>
    /// <param name="requestSupplied">Whether the expression came from the request rather than the server.</param>
    /// <param name="cancellationToken">Stops the evaluation, which also runs under the default evaluation limits.</param>
    public static IReadOnlyList<IElement> Select(this IElement element, string expression, bool requestSupplied, CancellationToken cancellationToken)
    {
        return TypedElementExtensions.Select(element, expression, new EvaluationContext { CancellationToken = cancellationToken, ExpressionIsRequestSupplied = requestSupplied }).ToList();
    }

    /// <summary>
    /// Selects nodes matching a FHIRPath expression, without cancellation. A separate overload rather than an optional
    /// parameter, so a two-argument call stays unambiguous against <see cref="TypedElementExtensions.Select"/>.
    /// </summary>
    public static IReadOnlyList<IElement> Select(this IElement element, string expression) =>
        element.Select(expression, CancellationToken.None);

    /// <summary>
    /// Parses an expression so that an invalid one is rejected up front rather than when first evaluated.
    /// </summary>
    /// <exception cref="Exception">The expression cannot be parsed.</exception>
    public static void EnsureParses(string expression)
    {
        TypedElementExtensions.PrecompileRequestSupplied(expression);
    }

    /// <summary>
    /// Evaluates a FHIRPath predicate expression against a node.
    /// Returns true if the expression evaluates to a truthy value.
    /// </summary>
    public static bool Predicate(this IElement element, string expression) =>
        element.Predicate(expression, CancellationToken.None);

    /// <summary>
    /// Evaluates a FHIRPath predicate expression against a node, stopping when <paramref name="cancellationToken"/> is cancelled.
    /// Returns true if the expression evaluates to a truthy value.
    /// </summary>
    public static bool Predicate(this IElement element, string expression, CancellationToken cancellationToken)
    {
        var results = element.Select(expression, cancellationToken);
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
    public static object? Scalar(this IElement element, string expression) =>
        element.Scalar(expression, CancellationToken.None);

    /// <summary>
    /// Evaluates a FHIRPath expression and returns a single scalar value, stopping when <paramref name="cancellationToken"/>
    /// is cancelled.
    /// </summary>
    public static object? Scalar(this IElement element, string expression, CancellationToken cancellationToken)
    {
        var results = element.Select(expression, cancellationToken);
        return results.Count > 0 ? results[0].Value : null;
    }
}
