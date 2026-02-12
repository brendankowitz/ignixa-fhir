// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using Ignixa.Anonymizer.Configuration;
using Ignixa.Anonymizer.Models;
using Ignixa.Anonymizer.Processors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ignixa.Anonymizer.Pipeline;

/// <summary>
/// Middleware that executes anonymization processors for matched rules.
/// Resolves processors via keyed DI services and applies them to matched elements.
/// </summary>
public sealed class ProcessorMiddleware : AnonymizerMiddleware
{
    private readonly Dictionary<string, IAnonymizerProcessor> _processorCache;
    private readonly ILogger<ProcessorMiddleware> _logger;

    public ProcessorMiddleware(
        IServiceProvider serviceProvider,
        IOptions<AnonymizerOptions> options,
        ILogger<ProcessorMiddleware> logger)
    {
        _logger = logger;

        // Pre-resolve and cache all processors referenced in configuration
        _processorCache = new Dictionary<string, IAnonymizerProcessor>(StringComparer.OrdinalIgnoreCase);

        foreach (var rule in options.Value.Rules)
        {
            var method = rule.Method.ToUpperInvariant();
            if (!_processorCache.ContainsKey(method))
            {
                var processor = serviceProvider.GetKeyedService<IAnonymizerProcessor>(method);
                if (processor is not null)
                {
                    _processorCache[method] = processor;
                    _logger.LogDebug("Cached processor for method '{Method}'", method);
                }
            }
        }

        _logger.LogInformation(
            "ProcessorMiddleware initialized with {ProcessorCount} cached processors",
            _processorCache.Count);
    }
    /// <inheritdoc />
    public override async ValueTask<Result<AnonymizationResult>> InvokeAsync(
        AnonymizerContext context,
        AnonymizerDelegate nextMiddleware,
        CancellationToken cancellationToken)
    {
        if (context.MatchedRules.Count == 0)
        {
            _logger.LogDebug("No matched rules to process");
            return await nextMiddleware(context, cancellationToken).ConfigureAwait(false);
        }

        _logger.LogDebug(
            "Processing {RuleCount} matched rules",
            context.MatchedRules.Count);

        foreach (var matchedRule in context.MatchedRules)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var method = matchedRule.Rule.Method.ToUpperInvariant();

            // Use cached processor instead of DI lookup
            if (!_processorCache.TryGetValue(method, out var processor))
            {
                _logger.LogWarning(
                    "No processor registered for method '{Method}', skipping rule {Path}",
                    method,
                    matchedRule.Rule.Path);

                context.AddWarning($"No processor registered for method '{method}'");
                continue;
            }

            var processorResult = await ProcessRuleAsync(
                context,
                matchedRule,
                processor,
                cancellationToken).ConfigureAwait(false);

            if (!processorResult.IsSuccess)
            {
                var errorHandling = context.Options.Processing?.ErrorHandling ?? ErrorHandlingMode.StopOnError;

                switch (errorHandling)
                {
                    case ErrorHandlingMode.FailFast:
                        return Result<AnonymizationResult>.Failure(processorResult.Error);

                    case ErrorHandlingMode.StopOnError:
                        return Result<AnonymizationResult>.Failure(processorResult.Error);

                    case ErrorHandlingMode.LogAndContinue:
                        _logger.LogWarning(
                            "Processor failed for rule {Path}: {Error}",
                            matchedRule.Rule.Path,
                            processorResult.Error.Message);
                        context.AddWarning($"Processor failed for rule '{matchedRule.Rule.Path}': {processorResult.Error.Message}");
                        continue;

                    default:
                        return Result<AnonymizationResult>.Failure(processorResult.Error);
                }
            }
        }

        return await nextMiddleware(context, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Processes all matched elements for a single rule.
    /// </summary>
    private async ValueTask<Result<bool>> ProcessRuleAsync(
        AnonymizerContext context,
        MatchedRule matchedRule,
        IAnonymizerProcessor processor,
        CancellationToken cancellationToken)
    {
        var method = matchedRule.Rule.Method.ToUpperInvariant();
        var settings = matchedRule.Rule.Settings;

        _logger.LogDebug(
            "Processing rule {Path} with method {Method} for {ElementCount} elements",
            matchedRule.Rule.Path,
            method,
            matchedRule.MatchedElements.Count);

        // Reuse ProcessorContext for all elements in this rule (reduces allocations)
        var processorContext = new ProcessorContext
        {
            ResourceId = context.Resource.Id,
            Settings = settings,
            VisitedNodes = context.VisitedNodes
        };

        foreach (var element in matchedRule.MatchedElements)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var location = element.Location ?? element.Name;

            if (context.VisitedNodes.Contains(location))
            {
                _logger.LogTrace("Skipping already processed node at {Location}", location);
                continue;
            }

            context.VisitedNodes.Add(location);

            try
            {
                var result = await processor.ProcessAsync(
                    context.Resource,
                    element,
                    processorContext,
                    cancellationToken).ConfigureAwait(false);

                if (!result.IsSuccess)
                {
                    return Result<bool>.Failure(result.Error);
                }

                if (result.Value.WasModified)
                {
                    context.IncrementOperationCount(result.Value.OperationType);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Processor {Method} failed for element at {Location}",
                    method,
                    location);

                return Result<bool>.Failure(new AnonymizerError(
                    "PROCESSOR_ERROR",
                    $"Processor '{method}' failed for element at '{location}': {ex.Message}",
                    ErrorSeverity.Error,
                    ex,
                    location));
            }
        }

        return Result<bool>.Success(true);
    }
}
