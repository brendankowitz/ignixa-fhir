/*
 * Copyright (c) 2025, Sparky Contributors
 *
 * Extension methods for ITypedElement to evaluate FhirPath expressions.
 * Provides API compatibility with Firely SDK FhirPath implementation.
 */

using System.Collections.Concurrent;
using Ignixa.FhirPath.Expressions;
using Ignixa.SourceNodeSerialization.Abstractions;

namespace Ignixa.FhirPath.Evaluation;

/// <summary>
/// Extension methods for evaluating FhirPath expressions on ITypedElement.
/// </summary>
public static class TypedElementExtensions
{
    // Thread-safe cache for compiled expressions (string -> Expression AST)
    private static readonly ConcurrentDictionary<string, Expression> _compiledExpressionCache = new();

    // Shared compiler instance
    private static readonly FhirPathCompiler _compiler = new FhirPathCompiler(preserveTrivia: false);

    // Shared evaluator instance
    private static readonly FhirPathEvaluator _evaluator = new FhirPathEvaluator();

    /// <summary>
    /// Compiles a FhirPath expression string, using cache for performance.
    /// </summary>
    private static Expression CompileExpression(string expression)
    {
        return _compiledExpressionCache.GetOrAdd(expression, expr => _compiler.Parse(expr));
    }

    /// <summary>
    /// Evaluates a FhirPath expression and returns matching elements.
    /// </summary>
    /// <param name="input">The root element to evaluate against</param>
    /// <param name="expression">FhirPath expression string</param>
    /// <param name="context">Optional evaluation context</param>
    /// <returns>Collection of elements that match the expression</returns>
    public static IEnumerable<ITypedElement> Select(this ITypedElement input, string expression, EvaluationContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);

        var compiledExpression = CompileExpression(expression);
        return _evaluator.Evaluate(input, compiledExpression, context);
    }

    /// <summary>
    /// Evaluates a FhirPath expression and returns a single scalar value.
    /// Returns null if the expression returns an empty collection or multiple values.
    /// </summary>
    /// <param name="input">The root element to evaluate against</param>
    /// <param name="expression">FhirPath expression string</param>
    /// <param name="context">Optional evaluation context</param>
    /// <returns>Single scalar value, or null if expression returns empty/multiple values</returns>
    public static object? Scalar(this ITypedElement input, string expression, EvaluationContext? context = null)
    {
        var results = input.Select(expression, context).ToList();

        if (results.Count == 1)
        {
            return results[0].Value;
        }

        return null;
    }

    /// <summary>
    /// Evaluates a FhirPath expression as a boolean predicate.
    /// Returns true if the expression evaluates to a single true value, false otherwise.
    /// </summary>
    /// <param name="input">The root element to evaluate against</param>
    /// <param name="expression">FhirPath expression string</param>
    /// <param name="context">Optional evaluation context</param>
    /// <returns>True if expression evaluates to true, false otherwise</returns>
    public static bool Predicate(this ITypedElement input, string expression, EvaluationContext? context = null)
    {
        return input.IsTrue(expression, context);
    }

    /// <summary>
    /// Evaluates a FhirPath expression and checks if result is true.
    /// </summary>
    /// <param name="input">The root element to evaluate against</param>
    /// <param name="expression">FhirPath expression string</param>
    /// <param name="context">Optional evaluation context</param>
    /// <returns>True if expression evaluates to a single true boolean value</returns>
    public static bool IsTrue(this ITypedElement input, string expression, EvaluationContext? context = null)
    {
        var results = input.Select(expression, context).ToList();
        return results.Count == 1 && results[0].Value is bool b && b;
    }

    /// <summary>
    /// Evaluates a FhirPath expression and checks if result matches the specified boolean value.
    /// </summary>
    /// <param name="input">The root element to evaluate against</param>
    /// <param name="expression">FhirPath expression string</param>
    /// <param name="value">Expected boolean value</param>
    /// <param name="context">Optional evaluation context</param>
    /// <returns>True if expression evaluates to the specified boolean value</returns>
    public static bool IsBoolean(this ITypedElement input, string expression, bool value, EvaluationContext? context = null)
    {
        var results = input.Select(expression, context).ToList();
        return results.Count == 1 && results[0].Value is bool b && b == value;
    }

    /// <summary>
    /// Clears the compiled expression cache.
    /// Useful for testing or memory management in long-running processes.
    /// </summary>
    public static void ClearCache()
    {
        _compiledExpressionCache.Clear();
    }
}
