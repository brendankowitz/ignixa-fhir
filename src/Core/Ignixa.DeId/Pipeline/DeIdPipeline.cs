// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using Ignixa.DeId.Configuration;
using Ignixa.DeId.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ignixa.DeId.Pipeline;

/// <summary>
/// Default implementation of the de-identification pipeline.
/// Builds and executes a chain of middleware for processing FHIR resources.
/// </summary>
public sealed class DeIdPipeline : IDeIdPipeline
{
    private readonly PipelineDelegate _pipeline;
    private readonly ILogger<DeIdPipeline> _logger;

    /// <summary>
    /// Creates a new de-identification pipeline with the specified handlers.
    /// </summary>
    /// <param name="options">DeId configuration options.</param>
    /// <param name="handlers">Array of handler components to execute.</param>
    /// <param name="logger">Logger for pipeline operations.</param>
    public DeIdPipeline(
        IOptions<DeIdOptions> options,
        DeIdPipelineHandler[] handlers,
        ILogger<DeIdPipeline> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(handlers);
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;
        _pipeline = BuildPipeline(handlers);

        _logger.LogDebug(
            "DeIdPipeline initialized with {HandlerCount} handler components",
            handlers.Length);
    }

    /// <inheritdoc />
    public async ValueTask<Result<DeIdResult>> ExecuteAsync(
        DeIdContext context,
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
            return Result<DeIdResult>.Failure(new DeIdError(
                "PIPELINE_ERROR",
                $"Pipeline execution failed: {ex.Message}",
                ErrorSeverity.Fatal,
                ex));
        }
    }

    /// <summary>
    /// Builds the handler pipeline by composing handlers in reverse order.
    /// This ensures handlers[0] executes first, handlers[1] second, etc.
    /// </summary>
    private static PipelineDelegate BuildPipeline(DeIdPipelineHandler[] handlers)
    {
        if (handlers.Length == 0)
        {
            return (ctx, _) => ValueTask.FromResult(
                Result<DeIdResult>.Success(ctx.BuildResult()));
        }

        PipelineDelegate current = (ctx, _) => ValueTask.FromResult(
            Result<DeIdResult>.Failure(new DeIdError(
                "PIPELINE_NOT_TERMINATED",
                "Pipeline was not terminated by any handler. Ensure OutputFormattingHandler is included.",
                ErrorSeverity.Error)));

        for (var i = handlers.Length - 1; i >= 0; i--)
        {
            var component = handlers[i];
            var next = current;
            current = (ctx, ct) => component.InvokeAsync(ctx, next, ct);
        }

        return current;
    }
}
