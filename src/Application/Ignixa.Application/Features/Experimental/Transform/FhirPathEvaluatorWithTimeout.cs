// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.Features.Experimental.Transform;

/// <summary>
/// Evaluates FHIRPath expressions for mapping transformations, backed by a compiled-expression cache.
/// </summary>
/// <remarks>
/// <para>
/// Despite the name, this class does not enforce an execution-time timeout. It previously ran
/// evaluation via <c>Task.Run(action, cancellationToken)</c> under a linked
/// <see cref="CancellationTokenSource"/> whose <c>CancelAfter(timeout)</c> was meant to abort a
/// runaway expression, translating the resulting <see cref="OperationCanceledException"/> into a
/// <see cref="TimeoutException"/>. That mechanism cannot work: <c>Task.Run</c> only honours a
/// cancellation token before its delegate starts running, and <see cref="FhirPathEvaluator.Evaluate"/>
/// is synchronous - once it starts, nothing inside this class can interrupt it. The timer could only
/// fire while the delegate was still queued on the thread pool, never because evaluation itself ran
/// long, which is the one case this class exists to guard against. Keeping a <c>TimeSpan timeout</c>
/// constructor parameter and a <see cref="TimeoutException"/> in the method signature advertised
/// protection that did not exist, so both were removed rather than left as decoration.
/// </para>
/// <para>
/// A real timeout requires cooperative cancellation inside the FHIRPath evaluator itself: the
/// evaluator would need to check a cancellation token (or a step/deadline budget) periodically while
/// walking the expression tree, the way long-running interpreters do. <c>Ignixa.FhirPath</c> does not
/// support that today. Until it does, the only guarantee this class can honestly make is that a
/// <see cref="CancellationToken"/> supplied to <see cref="EvaluateAsync"/> is honoured before
/// evaluation starts; it is not observed once evaluation is under way.
/// </para>
/// <para>
/// This is not purely academic: the caller is <c>TransformResourceHandler</c>'s <c>$transform</c>
/// operation, which evaluates FHIRPath expressions embedded in a StructureMap supplied by the request
/// itself (inline FML text, an inline StructureMap resource, or a canonical URL). An unbounded or
/// pathological expression in that map is a real DoS surface that this class cannot currently stop.
/// </para>
/// </remarks>
public class FhirPathEvaluatorWithTimeout
{
    private readonly FhirPathExpressionCache _expressionCache;
    private readonly FhirPathEvaluator _evaluator;
    private readonly ILogger<FhirPathEvaluatorWithTimeout> _logger;

    public FhirPathEvaluatorWithTimeout(
        FhirPathExpressionCache expressionCache,
        FhirPathEvaluator evaluator,
        ILogger<FhirPathEvaluatorWithTimeout> logger)
    {
        _expressionCache = expressionCache ?? throw new ArgumentNullException(nameof(expressionCache));
        _evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Evaluates a FHIRPath expression.
    /// </summary>
    /// <param name="expression">The FHIRPath expression string to evaluate</param>
    /// <param name="element">The root element to evaluate against</param>
    /// <param name="cancellationToken">
    /// Honoured only before evaluation starts - see the remarks on
    /// <see cref="FhirPathEvaluatorWithTimeout"/> for why it cannot interrupt evaluation once begun.
    /// </param>
    /// <returns>Collection of matching elements</returns>
    public async Task<IEnumerable<IElement>> EvaluateAsync(
        string expression,
        IElement element,
        CancellationToken cancellationToken)
    {
        // Get compiled expression from cache
        var compiled = _expressionCache.GetOrCompile(expression);

        try
        {
            // FhirPathEvaluator.Evaluate is synchronous, so we run it in a Task.
            // Evaluate returns a lazy enumerable: materializing it inside the Task is what makes the
            // work actually happen here, rather than on the caller's thread past every catch below.
            return await Task.Run<IEnumerable<IElement>>(
                () => _evaluator.Evaluate(element, compiled).ToList(),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("FHIRPath evaluation cancelled: {Expression}", expression);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "FHIRPath evaluation failed: {Expression}",
                expression);
            throw;
        }
    }

    /// <summary>
    /// Synchronous evaluation.
    /// Blocks until evaluation completes.
    /// </summary>
    /// <param name="expression">The FHIRPath expression string to evaluate</param>
    /// <param name="element">The root element to evaluate against</param>
    /// <returns>Collection of matching elements</returns>
    public IEnumerable<IElement> Evaluate(string expression, IElement element)
    {
        // For synchronous callers, use a default cancellation token
        return EvaluateAsync(expression, element, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }
}
