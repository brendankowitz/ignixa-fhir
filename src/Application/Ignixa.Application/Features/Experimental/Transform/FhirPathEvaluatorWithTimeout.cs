// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.Features.Experimental.Transform;

/// <summary>
/// FHIRPath evaluator with timeout protection for mapping transformations.
/// Prevents infinite loops or extremely slow expressions from blocking transformations.
/// </summary>
public class FhirPathEvaluatorWithTimeout
{
    private readonly FhirPathExpressionCache _expressionCache;
    private readonly FhirPathEvaluator _evaluator;
    private readonly TimeSpan _timeout;
    private readonly ILogger<FhirPathEvaluatorWithTimeout> _logger;

    public FhirPathEvaluatorWithTimeout(
        FhirPathExpressionCache expressionCache,
        FhirPathEvaluator evaluator,
        TimeSpan timeout,
        ILogger<FhirPathEvaluatorWithTimeout> logger)
    {
        _expressionCache = expressionCache ?? throw new ArgumentNullException(nameof(expressionCache));
        _evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
        _timeout = timeout;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentException("Timeout must be greater than zero", nameof(timeout));
        }
    }

    /// <summary>
    /// Evaluates a FHIRPath expression with timeout protection.
    /// </summary>
    /// <param name="expression">The FHIRPath expression string to evaluate</param>
    /// <param name="element">The root element to evaluate against</param>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>Collection of matching elements</returns>
    /// <exception cref="TimeoutException">Thrown when evaluation exceeds the configured timeout</exception>
    public async Task<IEnumerable<IElement>> EvaluateAsync(
        string expression,
        IElement element,
        CancellationToken cancellationToken)
    {
        // Get compiled expression from cache
        var compiled = _expressionCache.GetOrCompile(expression);

        // Create a linked cancellation token that includes the timeout
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_timeout);

        try
        {
            // Execute evaluation with timeout
            // FhirPathEvaluator.Evaluate is synchronous, so we run it in a Task
            var task = Task.Run(() => _evaluator.Evaluate(element, compiled), cts.Token);
            return await task;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Timeout occurred (timeout CTS was cancelled, but original token wasn't)
            _logger.LogWarning(
                "FHIRPath expression timed out after {TimeoutSeconds}s: {Expression}",
                _timeout.TotalSeconds,
                expression);

            throw new TimeoutException(
                $"FHIRPath expression timed out after {_timeout.TotalSeconds}s: {expression}");
        }
        catch (OperationCanceledException)
        {
            // Original cancellation token was cancelled (user-initiated)
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
    /// Synchronous evaluation with timeout protection.
    /// Blocks until evaluation completes or timeout occurs.
    /// </summary>
    /// <param name="expression">The FHIRPath expression string to evaluate</param>
    /// <param name="element">The root element to evaluate against</param>
    /// <returns>Collection of matching elements</returns>
    /// <exception cref="TimeoutException">Thrown when evaluation exceeds the configured timeout</exception>
    public IEnumerable<IElement> Evaluate(string expression, IElement element)
    {
        // For synchronous callers, use a default cancellation token
        return EvaluateAsync(expression, element, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }
}
