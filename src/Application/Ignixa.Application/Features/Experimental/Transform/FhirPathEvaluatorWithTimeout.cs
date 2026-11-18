// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.Application.Infrastructure;
using Ignixa.FhirPath.Evaluation;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.Features.Experimental.Transform;

/// <summary>
/// Evaluates the FHIRPath expressions of a <c>$transform</c> mapping under a time limit and the evaluation budget,
/// backed by a compiled-expression cache.
/// </summary>
/// <remarks>
/// <para>
/// <c>$transform</c> evaluates expressions embedded in a StructureMap the request itself supplies (inline FML text,
/// an inline StructureMap resource, or a canonical URL), so a pathological expression is a real DoS surface. Each
/// evaluation runs under <see cref="FhirPathEvaluationLimits.Default"/> with <see cref="Timeout"/> as its time
/// limit, and observes the caller's token; the FHIRPath evaluator checks both while it runs, so a runaway expression
/// is stopped mid-evaluation rather than after it. Exceeding either limit raises
/// <see cref="FhirPathResourceLimitExceededException"/>.
/// </para>
/// <para>
/// This class once ran evaluation through <c>Task.Run</c> under a timer that could only fire before the delegate
/// started, and so enforced nothing; its remarks said as much. The evaluator's own budget is what makes the name
/// honest now.
/// </para>
/// </remarks>
public class FhirPathEvaluatorWithTimeout
{
    private readonly FhirPathExpressionCache _expressionCache;
    private readonly FhirPathEvaluator _evaluator;
    private readonly ILogger<FhirPathEvaluatorWithTimeout> _logger;
    private readonly FhirPathEvaluationLimits _limits;

    /// <param name="expressionCache">Parsed expressions, shared across requests.</param>
    /// <param name="evaluator">The FHIRPath evaluator.</param>
    /// <param name="logger">Logs evaluation failures.</param>
    /// <param name="timeout">The time each evaluation, and the <c>$transform</c> as a whole, may take.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="timeout"/> is not positive.</exception>
    public FhirPathEvaluatorWithTimeout(
        FhirPathExpressionCache expressionCache,
        FhirPathEvaluator evaluator,
        ILogger<FhirPathEvaluatorWithTimeout> logger,
        TimeSpan timeout)
    {
        _expressionCache = expressionCache ?? throw new ArgumentNullException(nameof(expressionCache));
        _evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);

        Timeout = timeout;
        _limits = FhirPathEvaluationLimits.Default with { Timeout = timeout };
    }

    /// <summary>
    /// The configured time limit (<c>Experimental:Transform:TimeoutSeconds</c>), applied to each evaluation and, by
    /// <c>TransformResourceHandler</c>, to the transformation as a whole.
    /// </summary>
    public TimeSpan Timeout { get; }

    /// <summary>
    /// Evaluates a FHIRPath expression to completion.
    /// </summary>
    /// <param name="expression">The FHIRPath expression string to evaluate</param>
    /// <param name="element">The root element to evaluate against</param>
    /// <param name="cancellationToken">Observed throughout evaluation.</param>
    /// <returns>The matching elements, materialised so every evaluation error surfaces here.</returns>
    /// <exception cref="FhirPathResourceLimitExceededException">The evaluation exceeded its time limit or budget.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public IReadOnlyList<IElement> Evaluate(string expression, IElement element, CancellationToken cancellationToken)
    {
        var compiled = _expressionCache.GetOrCompile(expression);
        var context = new EvaluationContext { Limits = _limits, CancellationToken = cancellationToken };

        try
        {
            // Evaluate returns a lazy sequence; materialising it here is what makes the work, and its failures,
            // happen inside this method.
            return _evaluator.Evaluate(element, compiled, context).ToList();
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("FHIRPath evaluation cancelled: {Expression}", expression.SanitizeExpressionForLog());
            throw;
        }
        catch (FhirPathResourceLimitExceededException ex)
        {
            _logger.LogWarning(ex, "FHIRPath evaluation exceeded an evaluation limit: {Expression}", expression.SanitizeExpressionForLog());
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FHIRPath evaluation failed: {Expression}", expression.SanitizeExpressionForLog());
            throw;
        }
    }
}
