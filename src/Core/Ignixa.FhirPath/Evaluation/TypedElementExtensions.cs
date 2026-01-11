/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Extension methods for IElement to evaluate FhirPath expressions.
 * Provides API compatibility with Firely SDK FhirPath implementation.
 */

using System.Collections.Concurrent;
using Ignixa.FhirPath.Expressions;
using Ignixa.Abstractions;
using Ignixa.FhirPath.Parser;

namespace Ignixa.FhirPath.Evaluation;

/// <summary>
/// Extension methods for evaluating FhirPath expressions on IElement.
/// Integrates both AST caching and compiled delegate caching for optimal performance.
/// Compiled delegates provide 7x speedup for common patterns; complex expressions fall back to interpreter.
/// </summary>
public static class TypedElementExtensions
{
    // Thread-safe cache for compiled expressions (string -> Expression AST)
    private static readonly ConcurrentDictionary<string, Expression> _astCache = new();

    // Thread-safe cache for compiled delegates (Expression -> compiled delegate)
    // Key: Expression object hash code and expression string combined
    // Value: Compiled delegate or null if compilation not supported
    private static readonly ConcurrentDictionary<string, Func<IElement, EvaluationContext, IEnumerable<IElement>>?> _delegateCache = new();

    // Shared compiler instances
    private static readonly FhirPathParser AstParser = new FhirPathParser(preserveTrivia: false);
    private static readonly FhirPathDelegateCompiler _delegateCompiler = new FhirPathDelegateCompiler(new FhirPathEvaluator());

    // Shared evaluator instance
    private static readonly FhirPathEvaluator _evaluator = new FhirPathEvaluator();

    /// <summary>
    /// Parses and caches a FhirPath expression string to AST.
    /// </summary>
    private static Expression CompileExpressionToAst(string expression)
    {
        return _astCache.GetOrAdd(expression, expr => AstParser.Parse(expr));
    }

    /// <summary>
    /// Attempts to compile an AST expression to a delegate and caches the result.
    /// Returns the compiled delegate if successful, null if the expression pattern is not supported.
    /// </summary>
    private static Func<IElement, EvaluationContext, IEnumerable<IElement>>? CompileExpressionToDelegate(Expression ast, string expressionString)
    {
        // Use expression string as cache key (stable across invocations)
        return _delegateCache.GetOrAdd(expressionString, _ => _delegateCompiler.TryCompile(ast));
    }

    /// <summary>
    /// Evaluates a FhirPath expression and returns matching elements.
    /// Attempts to use compiled delegate for performance; falls back to interpreted evaluation if needed.
    /// </summary>
    /// <param name="input">The root element to evaluate against</param>
    /// <param name="expression">FhirPath expression string</param>
    /// <param name="context">Optional evaluation context</param>
    /// <returns>Collection of elements that match the expression</returns>
    public static IEnumerable<IElement> Select(this IElement input, string expression, EvaluationContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);

        context ??= new EvaluationContext();

        // Set the Resource and RootResource for FHIR-specific functions like getResourceKey()
        // If input is the root resource element, set both to the input (immutable pattern)
        if (context.Resource is null || context.RootResource is null)
        {
            context = context with
            {
                Resource = context.Resource ?? input,
                RootResource = context.RootResource ?? input
            };
        }

        // 1. Parse expression to AST (cached)
        var ast = CompileExpressionToAst(expression);

        // 2. Try to compile to delegate (cached, may return null)
        var compiledDelegate = CompileExpressionToDelegate(ast, expression);

        // 3. If compiled, execute delegate; otherwise fall back to interpreter
        if (compiledDelegate != null)
        {
            return compiledDelegate(input, context);
        }

        // Fallback: Use interpreted evaluation
        return _evaluator.Evaluate(input, ast, context);
    }

    /// <summary>
    /// Evaluates a FhirPath expression and returns a single scalar value.
    /// Returns null if the expression returns an empty collection or multiple values.
    /// </summary>
    /// <param name="input">The root element to evaluate against</param>
    /// <param name="expression">FhirPath expression string</param>
    /// <param name="context">Optional evaluation context</param>
    /// <returns>Single scalar value, or null if expression returns empty/multiple values</returns>
    public static object? Scalar(this IElement input, string expression, EvaluationContext? context = null)
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
    public static bool Predicate(this IElement input, string expression, EvaluationContext? context = null)
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
    public static bool IsTrue(this IElement input, string expression, EvaluationContext? context = null)
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
    public static bool IsBoolean(this IElement input, string expression, bool value, EvaluationContext? context = null)
    {
        var results = input.Select(expression, context).ToList();
        return results.Count == 1 && results[0].Value is bool b && b == value;
    }

    /// <summary>
    /// Clears all expression caches (AST and compiled delegates).
    /// Useful for testing or memory management in long-running processes.
    /// </summary>
    public static void ClearCache()
    {
        _astCache.Clear();
        _delegateCache.Clear();
    }
}
