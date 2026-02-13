// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.Anonymizer.Configuration;
using Ignixa.Anonymizer.Extensions;
using Ignixa.Anonymizer.Models;
using Ignixa.Anonymizer.Processors;
using Ignixa.Anonymizer.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ignixa.Anonymizer.Pipeline;

/// <summary>
/// Handler that executes anonymization processors for matched rules.
/// Resolves processors via keyed DI services and applies them to matched elements.
/// </summary>
internal sealed class ProcessorHandler : AnonymizerPipelineHandler
{
    private readonly Dictionary<string, IAnonymizerProcessor> _processorCache;
    private readonly ILogger<ProcessorHandler> _logger;

    public ProcessorHandler(
        IServiceProvider serviceProvider,
        IOptions<AnonymizerOptions> options,
        ILogger<ProcessorHandler> logger)
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
            "ProcessorHandler initialized with {ProcessorCount} cached processors",
            _processorCache.Count);
    }
    /// <inheritdoc />
    public override async ValueTask<Result<AnonymizationResult>> InvokeAsync(
        AnonymizerContext context,
        PipelineDelegate nextHandler,
        CancellationToken cancellationToken)
    {
        if (context.MatchedRules.Count == 0)
        {
            _logger.LogDebug("No matched rules to process");
            return await nextHandler(context, cancellationToken).ConfigureAwait(false);
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

        CleanupDocument(context.Resource.MutableNode);

        return await nextHandler(context, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Processes all matched elements for a single rule.
    /// For complex elements where the processor returns WasModified=false,
    /// walks descendants and applies the processor to each unvisited descendant.
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

        var processorContext = new ProcessorContext
        {
            ResourceId = context.Resource.Id,
            Settings = settings,
            VisitedNodes = context.VisitedNodes
        };

        foreach (var element in matchedRule.MatchedElements)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var processResult = await ProcessElementAsync(
                context, element, processor, processorContext, method, cancellationToken)
                .ConfigureAwait(false);

            if (!processResult.IsSuccess)
            {
                return processResult;
            }
        }

        return Result<bool>.Success(true);
    }

    /// <summary>
    /// Processes a single element. If the processor returns WasModified=false
    /// for a non-primitive element, walks its descendants and applies the
    /// processor to each unvisited child.
    /// </summary>
    private async ValueTask<Result<bool>> ProcessElementAsync(
        AnonymizerContext context,
        IElement element,
        IAnonymizerProcessor processor,
        ProcessorContext processorContext,
        string method,
        CancellationToken cancellationToken)
    {
        var location = element.Location ?? element.Name;

        if (context.VisitedNodes.Contains(location))
        {
            _logger.LogTrace("Skipping already processed node at {Location}", location);
            return Result<bool>.Success(true);
        }

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

            context.VisitedNodes.Add(location);

            if (result.Value.WasModified)
            {
                context.IncrementOperationCount(result.Value.OperationType);
                return Result<bool>.Success(true);
            }

            if (element.IsPrimitiveElement())
            {
                return Result<bool>.Success(true);
            }

            var children = element.Children().ToList();
            foreach (var child in children)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var childResult = await ProcessElementAsync(
                    context, child, processor, processorContext, method, cancellationToken)
                    .ConfigureAwait(false);

                if (!childResult.IsSuccess)
                {
                    return childResult;
                }
            }

            CleanupEmptyProperties(element);
            RemoveIfEmpty(element);
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

        return Result<bool>.Success(true);
    }

    /// <summary>
    /// Scans a complex element's underlying JsonObject for properties that became empty
    /// after child processing (empty arrays, empty objects) and removes them.
    /// Also removes orphaned shadow properties (_xxx) whose main property was removed.
    /// Repeats until no more cleanup is needed to handle cascading emptiness.
    /// </summary>
    private static void CleanupEmptyProperties(IElement element)
    {
        if (element.Meta<JsonNode>() is not JsonObject obj)
        {
            return;
        }

        bool removed;
        do
        {
            removed = false;
            var propertiesToRemove = new List<string>();

            foreach (var kvp in obj)
            {
                switch (kvp.Value)
                {
                    case JsonArray arr when arr.Count == 0:
                        propertiesToRemove.Add(kvp.Key);
                        break;
                    case JsonObject child when child.Count == 0:
                        propertiesToRemove.Add(kvp.Key);
                        break;
                }
            }

            // Remove orphaned FHIR shadow properties (_xxx where xxx was removed)
            foreach (var kvp in obj)
            {
                if (kvp.Key.StartsWith('_') && kvp.Key.Length > 1)
                {
                    var mainProperty = kvp.Key[1..];
                    if (!obj.ContainsKey(mainProperty))
                    {
                        propertiesToRemove.Add(kvp.Key);
                    }
                }
            }

            foreach (var key in propertiesToRemove)
            {
                obj.Remove(key);
                removed = true;
            }
        } while (removed);
    }

    /// <summary>
    /// Removes a complex element from its parent if all its properties/items have been removed.
    /// Handles both JsonObject (removes if no properties) and JsonArray parents (removes empty arrays).
    /// </summary>
    private static void RemoveIfEmpty(IElement element)
    {
        var jsonNode = element.Meta<JsonNode>();

        switch (jsonNode)
        {
            case JsonObject obj when obj.Count == 0:
                ElementMutationTool.RemoveProperty(element);
                break;
            case JsonArray arr when arr.Count == 0:
                ElementMutationTool.RemoveProperty(element);
                break;
        }
    }

    /// <summary>
    /// Performs a document-level cleanup pass on the JSON tree after all rules have been processed.
    /// Recursively removes empty arrays, empty objects, and orphaned FHIR shadow properties (_xxx).
    /// Repeats until no more changes occur to handle cascading emptiness.
    /// </summary>
    private static void CleanupDocument(JsonObject root)
    {
        bool changed;
        do
        {
            changed = CleanupJsonNode(root);
        } while (changed);
    }

    /// <summary>
    /// Recursively walks a JsonNode tree, removing empty arrays, empty objects,
    /// and orphaned shadow properties. Returns true if any changes were made.
    /// </summary>
    private static bool CleanupJsonNode(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                return CleanupJsonObject(obj);
            case JsonArray arr:
                return CleanupJsonArray(arr);
            default:
                return false;
        }
    }

    private static bool CleanupJsonObject(JsonObject obj)
    {
        bool changed = false;

        // First recurse into children
        foreach (var kvp in obj.ToList())
        {
            if (CleanupJsonNode(kvp.Value))
            {
                changed = true;
            }
        }

        var toRemove = new List<string>();

        foreach (var kvp in obj)
        {
            switch (kvp.Value)
            {
                case JsonArray arr when arr.Count == 0:
                    toRemove.Add(kvp.Key);
                    break;
                case JsonObject child when child.Count == 0:
                    toRemove.Add(kvp.Key);
                    break;
            }
        }

        // Remove orphaned FHIR shadow properties
        foreach (var kvp in obj)
        {
            if (kvp.Key.StartsWith('_') && kvp.Key.Length > 1)
            {
                var mainProperty = kvp.Key[1..];
                if (!obj.ContainsKey(mainProperty))
                {
                    toRemove.Add(kvp.Key);
                }
            }
        }

        foreach (var key in toRemove)
        {
            obj.Remove(key);
            changed = true;
        }

        return changed;
    }

    private static bool CleanupJsonArray(JsonArray arr)
    {
        bool changed = false;

        for (int i = arr.Count - 1; i >= 0; i--)
        {
            if (CleanupJsonNode(arr[i]))
            {
                changed = true;
            }

            // Remove empty objects from arrays
            if (arr[i] is JsonObject innerObj && innerObj.Count == 0)
            {
                arr.RemoveAt(i);
                changed = true;
            }
        }

        return changed;
    }
}
