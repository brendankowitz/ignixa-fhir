// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using Ignixa.Anonymizer.Configuration;
using Ignixa.Anonymizer.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ignixa.Anonymizer.Pipeline;

/// <summary>
/// Default implementation of the anonymization pipeline.
/// Builds and executes a chain of middleware for processing FHIR resources.
/// </summary>
public sealed class AnonymizerPipeline : IAnonymizerPipeline
{
    private readonly AnonymizerDelegate _pipeline;
    private readonly ILogger<AnonymizerPipeline> _logger;

    /// <summary>
    /// Creates a new anonymization pipeline with the specified middleware.
    /// </summary>
    /// <param name="options">Anonymizer configuration options.</param>
    /// <param name="middleware">Array of middleware components to execute.</param>
    /// <param name="logger">Logger for pipeline operations.</param>
    public AnonymizerPipeline(
        IOptions<AnonymizerOptions> options,
        AnonymizerMiddleware[] middleware,
        ILogger<AnonymizerPipeline> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(middleware);
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;
        _pipeline = BuildPipeline(middleware);

        _logger.LogDebug(
            "AnonymizerPipeline initialized with {MiddlewareCount} middleware components",
            middleware.Length);
    }

    /// <inheritdoc />
    public async ValueTask<Result<AnonymizationResult>> ExecuteAsync(
        AnonymizerContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        _logger.LogDebug(
            "Executing pipeline for resource type {ResourceType}",
            context.Resource.ResourceType);

        try
        {
            return await _pipeline(context, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Pipeline execution was cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Pipeline execution failed with unexpected error");
            return Result<AnonymizationResult>.Failure(new AnonymizerError(
                "PIPELINE_ERROR",
                $"Pipeline execution failed: {ex.Message}",
                ErrorSeverity.Fatal,
                ex));
        }
    }

    /// <summary>
    /// Builds the middleware pipeline by composing middleware in reverse order.
    /// This ensures middleware[0] executes first, middleware[1] second, etc.
    /// </summary>
    private static AnonymizerDelegate BuildPipeline(AnonymizerMiddleware[] middleware)
    {
        if (middleware.Length == 0)
        {
            return (ctx, _) => ValueTask.FromResult(
                Result<AnonymizationResult>.Success(ctx.BuildResult()));
        }

        AnonymizerDelegate current = (ctx, _) => ValueTask.FromResult(
            Result<AnonymizationResult>.Failure(new AnonymizerError(
                "PIPELINE_NOT_TERMINATED",
                "Pipeline was not terminated by any middleware. Ensure OutputFormattingMiddleware is included.",
                ErrorSeverity.Error)));

        for (var i = middleware.Length - 1; i >= 0; i--)
        {
            var component = middleware[i];
            var next = current;
            current = (ctx, ct) => component.InvokeAsync(ctx, next, ct);
        }

        return current;
    }
}
