/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * FHIRPath integration for FHIR Mapping Language.
 * Bridges the mapping evaluator with the Ignixa.FhirPath library.
 */

using Ignixa.FhirPath;
using Ignixa.FhirPath.Evaluation;
using Ignixa.Abstractions;
using Ignixa.FhirPath.Parser;
using Ignixa.FhirPath.Visitors;
using Ignixa.FhirPath.Expressions;

namespace Ignixa.FhirMappingLanguage.Evaluation;

/// <summary>
/// Provides FHIRPath evaluation capabilities for mapping expressions.
/// </summary>
internal class FhirPathIntegration
{
    internal const int DefaultCacheCapacity = 512;

    private readonly FhirPathParser _parser;
    private readonly FhirPathEvaluator _evaluator;
    private readonly FhirPathEvaluationLimits _limits;
    private readonly BoundedExpressionCache<Ignixa.FhirPath.Expressions.Expression>? _expressionCache;

    /// <summary>
    /// Creates a new FhirPathIntegration instance.
    /// </summary>
    /// <param name="cacheExpressions">Whether to cache compiled expressions for performance</param>
    /// <param name="limits">The limits each evaluation runs under; <see cref="FhirPathEvaluationLimits.Default"/> when null.</param>
    /// <param name="cacheCapacity">Entries retained per cache generation. Expression text comes from the map being evaluated, so the cache evicts rather than grows.</param>
    public FhirPathIntegration(bool cacheExpressions = true, FhirPathEvaluationLimits? limits = null, int cacheCapacity = DefaultCacheCapacity)
    {
        _parser = new();
        _evaluator = new();
        _limits = limits ?? FhirPathEvaluationLimits.Default;
        _expressionCache = cacheExpressions ? new BoundedExpressionCache<Ignixa.FhirPath.Expressions.Expression>(cacheCapacity) : null;
    }

    /// <summary>
    /// Evaluates a FHIRPath expression against an element.
    /// </summary>
    /// <param name="expression">The FHIRPath expression to evaluate</param>
    /// <param name="element">The element to evaluate against</param>
    /// <param name="mappingContext">Supplies mapping variables, and the transformation's cancellation token.</param>
    /// <returns>The result elements</returns>
    /// <exception cref="OperationCanceledException">The transformation was cancelled or timed out.</exception>
    /// <exception cref="FhirPathResourceLimitExceededException">The expression exceeded an evaluation limit.</exception>
    public IEnumerable<IElement> Evaluate(string expression, IElement element, MappingContext? mappingContext = null)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return [];
        }

        try
        {
            // Get or compile the expression
            var compiledExpression = GetOrCompileExpression(expression);
            var evaluationContext = new EvaluationContext
            {
                Limits = _limits,
                CancellationToken = mappingContext?.CancellationToken ?? CancellationToken.None,
            };
            if (mappingContext is not null)
            {
                var variables = new Dictionary<string, IReadOnlyList<IElement>>(StringComparer.Ordinal);
                compiledExpression.AcceptVisitor(new MappingVariableVisitor(mappingContext), variables);
                foreach (var variable in variables)
                {
                    evaluationContext = evaluationContext.WithEnvironmentVariable(variable.Key, variable.Value);
                }
            }

            // Materialized here so evaluation errors surface inside this try rather than at the
            // caller's first enumeration, which would bypass the wrapping below entirely.
            return _evaluator.Evaluate(element, compiledExpression, evaluationContext).ToList();
        }
        catch (OperationCanceledException)
        {
            // The transformation's own timeout or the caller's cancellation, which MappingEvaluator reports.
            throw;
        }
        catch (FhirPathResourceLimitExceededException ex)
        {
            // Kept in its own type: "the map asked for more than an evaluation allows" is a capacity
            // outcome a host reports differently from an ill-formed expression.
            throw new FhirPathResourceLimitExceededException($"Failed to evaluate FHIRPath expression: {expression}", ex);
        }
        catch (FhirPathEvaluationException ex)
        {
            // Same message and inner exception as the general case, but the type stays distinguishable
            // so callers can tell "this expression is ill-formed for its data" from an engine defect.
            throw new FhirPathEvaluationException($"Failed to evaluate FHIRPath expression: {expression}", ex);
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
    public bool EvaluateBoolean(string expression, IElement element)
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
    public object? EvaluateScalar(string expression, IElement element)
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
            return _expressionCache.GetOrAdd(expression, _parser.Parse);
        }

        return _parser.Parse(expression);
    }

    internal int CachedExpressionCount => _expressionCache?.Count ?? 0;

    /// <summary>
    /// Clears the expression cache.
    /// </summary>
    public void ClearCache()
    {
        _expressionCache?.Clear();
    }

    internal static IElement CreateSystemQuantity(FhirQuantity quantity) =>
        new FhirPathEvaluator().VisitQuantity(
            new QuantityExpression(quantity.Value, quantity.Unit), new EvaluationContext()).Single();

    private sealed class MappingVariableVisitor(MappingContext mappingContext)
        : DefaultFhirPathExpressionVisitor<Dictionary<string, IReadOnlyList<IElement>>, bool>
    {
        public override bool VisitVariable(VariableRefExpression expression, Dictionary<string, IReadOnlyList<IElement>> context)
        {
            if (mappingContext.ResolveElements(expression.Name) is { } values)
            {
                context[expression.Name] = values;
            }
            return true;
        }
    }
}
