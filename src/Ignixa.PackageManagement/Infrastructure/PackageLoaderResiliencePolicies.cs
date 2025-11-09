using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;
using Polly.Extensions.Http;
using Polly.Timeout;

namespace Ignixa.PackageManagement.Infrastructure;

/// <summary>
/// Defines resilience policies for FHIR package downloads.
/// Implements retry and circuit breaker patterns for robustness.
/// </summary>
public static class PackageLoaderResiliencePolicies
{
    /// <summary>
    /// Creates a retry policy for transient HTTP failures.
    /// Retries up to 3 times with exponential backoff (1s, 2s, 4s).
    /// </summary>
    /// <param name="logger">Logger for policy events</param>
    /// <returns>IAsyncPolicy for use with HttpClient</returns>
    public static IAsyncPolicy<HttpResponseMessage> CreateRetryPolicy(ILogger logger)
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .Or<HttpRequestException>(ex => ex.StatusCode == System.Net.HttpStatusCode.RequestTimeout)
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: attempt =>
                {
                    // Exponential backoff: 1s, 2s, 4s
                    var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));
                    logger.LogInformation(
                        "Retrying package download (attempt {Attempt}/3). Waiting {DelayMs}ms",
                        attempt + 1, delay.TotalMilliseconds);
                    return delay;
                },
                onRetry: (outcome, delay, retryCount, context) =>
                {
                    if (outcome.Exception != null)
                    {
                        logger.LogWarning(
                            outcome.Exception,
                            "Package download failed (attempt {Attempt}). Retrying after {DelayMs}ms. Error: {ErrorMessage}",
                            retryCount, delay.TotalMilliseconds, outcome.Exception.Message);
                    }
                    else if (outcome.Result != null)
                    {
                        logger.LogWarning(
                            "Package download returned {StatusCode} (attempt {Attempt}). Retrying after {DelayMs}ms",
                            outcome.Result.StatusCode, retryCount, delay.TotalMilliseconds);
                    }
                });
    }

    /// <summary>
    /// Creates a circuit breaker policy to prevent cascading failures.
    /// Opens circuit after 5 consecutive failures, waits 30 seconds before trying again.
    /// </summary>
    /// <param name="logger">Logger for policy events</param>
    /// <returns>IAsyncPolicy for use with HttpClient</returns>
    public static IAsyncPolicy<HttpResponseMessage> CreateCircuitBreakerPolicy(ILogger logger)
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .Or<HttpRequestException>(ex => ex.StatusCode == System.Net.HttpStatusCode.RequestTimeout)
            .CircuitBreakerAsync<HttpResponseMessage>(
                handledEventsAllowedBeforeBreaking: 5,
                durationOfBreak: TimeSpan.FromSeconds(30),
                onBreak: (outcome, duration) =>
                {
                    logger.LogError(
                        "Circuit breaker opened for package downloads. Will retry after {DurationSeconds}s. Reason: {Reason}",
                        duration.TotalSeconds,
                        outcome.Exception?.Message ?? $"HTTP {outcome.Result?.StatusCode}");
                },
                onReset: () =>
                {
                    logger.LogInformation("Circuit breaker reset. Package downloads resumed.");
                },
                onHalfOpen: () =>
                {
                    logger.LogInformation("Circuit breaker half-open. Testing package download connectivity.");
                });
    }

    /// <summary>
    /// Creates a timeout policy for package downloads.
    /// Fails if download takes longer than 5 minutes.
    /// </summary>
    /// <param name="logger">Logger for policy events</param>
    /// <returns>IAsyncPolicy for use with HttpClient</returns>
    public static IAsyncPolicy<HttpResponseMessage> CreateTimeoutPolicy(ILogger logger)
    {
        return Policy.TimeoutAsync<HttpResponseMessage>(
            timeout: TimeSpan.FromMinutes(5),
            timeoutStrategy: TimeoutStrategy.Optimistic,
            onTimeoutAsync: (outcome, delay, context, ct) =>
            {
                logger.LogError(
                    "Package download timed out after {TimeoutMs}ms",
                    delay.TotalMilliseconds);
                return Task.CompletedTask;
            });
    }

    /// <summary>
    /// Combines all resilience policies into a comprehensive strategy.
    /// Policies are executed in order: Timeout → Retry → Circuit Breaker
    /// </summary>
    /// <param name="logger">Logger for policy events</param>
    /// <returns>Combined IAsyncPolicy for use with HttpClient</returns>
    public static IAsyncPolicy<HttpResponseMessage> CreateCombinedPolicy(ILogger logger)
    {
        var retryPolicy = CreateRetryPolicy(logger);
        var circuitBreakerPolicy = CreateCircuitBreakerPolicy(logger);
        var timeoutPolicy = CreateTimeoutPolicy(logger);

        // Stack policies: Timeout wraps Retry, which wraps Circuit Breaker
        // This ensures timeout applies to the entire retry sequence
        return Policy.WrapAsync(timeoutPolicy, retryPolicy, circuitBreakerPolicy);
    }
}
