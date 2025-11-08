/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * FHIRPath integration for FHIR Mapping Language.
 * Bridges the mapping evaluator with the Ignixa.FhirPath library.
 */

using Ignixa.FhirPath;
using Ignixa.FhirPath.Evaluation;
using Ignixa.Serialization.Abstractions;

namespace Ignixa.FhirMappingLanguage.Evaluation;

/// <summary>
/// Provides FHIRPath evaluation capabilities for mapping expressions.
/// </summary>
public class FhirPathIntegration
{
    private readonly FhirPathCompiler _compiler;
    private readonly FhirPathEvaluator _evaluator;
    private readonly Dictionary<string, Ignixa.FhirPath.Expressions.Expression> _expressionCache;

    /// <summary>
    /// Creates a new FhirPathIntegration instance.
    /// </summary>
    /// <param name="cacheExpressions">Whether to cache compiled expressions for performance</param>
    public FhirPathIntegration(bool cacheExpressions = true)
    {
        _compiler = new FhirPathCompiler();
        _evaluator = new FhirPathEvaluator();
        _expressionCache = cacheExpressions ? new Dictionary<string, Ignixa.FhirPath.Expressions.Expression>() : null!;
    }

    /// <summary>
    /// Evaluates a FHIRPath expression against an element.
    /// </summary>
    /// <param name="expression">The FHIRPath expression to evaluate</param>
    /// <param name="element">The element to evaluate against</param>
    /// <returns>The result elements</returns>
    public IEnumerable<ITypedElement> Evaluate(string expression, ITypedElement element)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return Enumerable.Empty<ITypedElement>();
        }

        try
        {
            // Get or compile the expression
            var compiledExpression = GetOrCompileExpression(expression);

            // Evaluate
            return _evaluator.Evaluate(element, compiledExpression);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to evaluate FHIRPath expression: {expression}", ex);
        }
    }

    /// <summary>
    /// Evaluates a FHIRPath expression and returns a boolean result.
    /// Used for where and check conditions.
    /// </summary>
    /// <param name="expression">The FHIRPath expression</param>
    /// <param name="element">The element to evaluate against</param>
    /// <returns>True if the condition is satisfied, false otherwise</returns>
    public bool EvaluateBoolean(string expression, ITypedElement element)
    {
        var results = Evaluate(expression, element).ToList();

        if (results.Count == 0)
        {
            return false;
        }

        // Check if the first result is a boolean true
        return results[0].Value is bool b && b;
    }

    /// <summary>
    /// Evaluates a FHIRPath expression and returns a single scalar value.
    /// Used for extracting values in transforms.
    /// </summary>
    /// <param name="expression">The FHIRPath expression</param>
    /// <param name="element">The element to evaluate against</param>
    /// <returns>The scalar value or null</returns>
    public object? EvaluateScalar(string expression, ITypedElement element)
    {
        var results = Evaluate(expression, element).ToList();
        return results.Count > 0 ? results[0].Value : null;
    }

    /// <summary>
    /// Gets a compiled expression from cache or compiles it.
    /// </summary>
    private Ignixa.FhirPath.Expressions.Expression GetOrCompileExpression(string expression)
    {
        if (_expressionCache != null)
        {
            if (_expressionCache.TryGetValue(expression, out var cached))
            {
                return cached;
            }

            var compiled = _compiler.Parse(expression);
            _expressionCache[expression] = compiled;
            return compiled;
        }

        return _compiler.Parse(expression);
    }

    /// <summary>
    /// Clears the expression cache.
    /// </summary>
    public void ClearCache()
    {
        _expressionCache?.Clear();
    }
}
