// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using Ignixa.Domain.Models;
using Ignixa.Search.Expressions;

namespace Ignixa.DataLayer.SqlEntityFramework.Search;

/// <summary>
/// Processes :iterate modifiers on _include and _revinclude expressions.
/// Recursively applies includes until no new resources are found or max depth is reached.
/// </summary>
public class IterateProcessor
{
    private readonly IncludeProcessor _includeProcessor;
    private readonly RevIncludeProcessor _revIncludeProcessor;
    private readonly ILogger<IterateProcessor> _logger;
    private const int MaxIterationDepth = 10; // Prevent infinite loops

    /// <summary>
    /// Initializes a new instance of the <see cref="IterateProcessor"/> class.
    /// </summary>
    /// <param name="includeProcessor">The include processor for forward includes.</param>
    /// <param name="revIncludeProcessor">The revinclude processor for reverse includes.</param>
    /// <param name="logger">Logger instance.</param>
    public IterateProcessor(
        IncludeProcessor includeProcessor,
        RevIncludeProcessor revIncludeProcessor,
        ILogger<IterateProcessor> logger)
    {
        _includeProcessor = includeProcessor ?? throw new ArgumentNullException(nameof(includeProcessor));
        _revIncludeProcessor = revIncludeProcessor ?? throw new ArgumentNullException(nameof(revIncludeProcessor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Processes :iterate modifiers on include expressions.
    /// Recursively fetches included resources until no new resources are found.
    /// </summary>
    /// <param name="mainResults">The main search results to start from.</param>
    /// <param name="iterateExpressions">The include/revinclude expressions with :iterate modifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>All resources discovered through recursive iteration.</returns>
    public async Task<List<SearchEntryResult>> ProcessIteratesAsync(
        IReadOnlyList<SearchEntryResult> mainResults,
        IReadOnlyList<IncludeExpression> iterateExpressions,
        CancellationToken ct)
    {
        if (mainResults.Count == 0 || iterateExpressions.Count == 0)
        {
            return new List<SearchEntryResult>();
        }

        _logger.LogDebug("Processing {Count} :iterate expressions for {ResultCount} main results",
            iterateExpressions.Count, mainResults.Count);

        // Track all resources we've seen (to avoid duplicates and infinite loops)
        var allResources = new Dictionary<string, SearchEntryResult>();
        foreach (var resource in mainResults)
        {
            var key = $"{resource.ResourceType}/{resource.ResourceId}";
            allResources[key] = resource;
        }

        // Separate forward and reverse iterate expressions
        var forwardIterates = iterateExpressions.Where(e => e.Iterate && !e.Reversed).ToList();
        var reverseIterates = iterateExpressions.Where(e => e.Iterate && e.Reversed).ToList();

        // Start with main results as current batch
        var currentBatch = mainResults.ToList();
        int iterationDepth = 0;

        while (currentBatch.Count > 0 && iterationDepth < MaxIterationDepth)
        {
            iterationDepth++;
            _logger.LogDebug("Iteration {Depth}: Processing {Count} resources", iterationDepth, currentBatch.Count);

            var newlyDiscovered = new List<SearchEntryResult>();

            // Process forward includes on current batch
            if (forwardIterates.Count > 0)
            {
                // Convert SearchEntryResult to resource identities for the include processor
                var resourceIdentities = currentBatch
                    .Select(r => (r.ResourceType, r.ResourceId))
                    .ToList();

                // Pass forIteration: true to process iterate expressions (not filter them out)
                var forwardIncludes = await _includeProcessor.ProcessIncludesAsync(
                    resourceIdentities,
                    forwardIterates,
                    ct,
                    forIteration: true);

                foreach (var resource in forwardIncludes)
                {
                    var key = $"{resource.ResourceType}/{resource.ResourceId}";
                    if (!allResources.ContainsKey(key))
                    {
                        allResources[key] = resource;
                        newlyDiscovered.Add(resource);
                    }
                }

                _logger.LogDebug("Iteration {Depth}: Found {Count} new forward includes", iterationDepth, forwardIncludes.Count);
            }

            // Process reverse includes on current batch
            if (reverseIterates.Count > 0)
            {
                // Convert SearchEntryResult to resource identities for the revinclude processor
                var resourceIdentities = currentBatch
                    .Select(r => (r.ResourceType, r.ResourceId))
                    .ToList();

                // Pass forIteration: true to process iterate expressions (not filter them out)
                var reverseIncludes = await _revIncludeProcessor.ProcessRevIncludesAsync(
                    resourceIdentities,
                    reverseIterates,
                    ct,
                    forIteration: true);

                foreach (var resource in reverseIncludes)
                {
                    var key = $"{resource.ResourceType}/{resource.ResourceId}";
                    if (!allResources.ContainsKey(key))
                    {
                        allResources[key] = resource;
                        newlyDiscovered.Add(resource);
                    }
                }

                _logger.LogDebug("Iteration {Depth}: Found {Count} new reverse includes", iterationDepth, reverseIncludes.Count);
            }

            // If no new resources discovered, we're done
            if (newlyDiscovered.Count == 0)
            {
                _logger.LogDebug("Iteration {Depth}: No new resources found, stopping", iterationDepth);
                break;
            }

            // Next iteration processes only newly discovered resources
            currentBatch = newlyDiscovered;
        }

        if (iterationDepth >= MaxIterationDepth)
        {
            _logger.LogWarning("Reached maximum iteration depth ({MaxDepth}), stopping to prevent infinite loop", MaxIterationDepth);
        }

        // Return all discovered resources (excluding main results which are already in the search results)
        var iteratedResources = allResources.Values
            .Where(r => !mainResults.Any(m => m.ResourceType == r.ResourceType && m.ResourceId == r.ResourceId))
            .ToList();

        _logger.LogDebug("Total iterated resources discovered: {Count} (across {Iterations} iterations)",
            iteratedResources.Count, iterationDepth);

        return iteratedResources;
    }
}
