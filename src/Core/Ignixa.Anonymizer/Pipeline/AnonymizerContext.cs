// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Collections.Immutable;
using System.Diagnostics;
using Ignixa.Abstractions;
using Ignixa.Anonymizer.Configuration;
using Ignixa.Anonymizer.Models;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Anonymizer.Pipeline;

/// <summary>
/// Context object passed through the anonymization pipeline.
/// Contains the resource being processed and mutable state for tracking operations.
/// </summary>
public sealed class AnonymizerContext
{
    private readonly Stopwatch _stopwatch;

    /// <summary>
    /// The FHIR resource being anonymized.
    /// </summary>
    public ResourceJsonNode Resource { get; }

    /// <summary>
    /// The root element of the resource.
    /// </summary>
    public IElement Element { get; }

    /// <summary>
    /// The FHIR schema provider for parsing and validation.
    /// </summary>
    public IFhirSchemaProvider Schema { get; }

    /// <summary>
    /// Per-request settings for anonymization behavior.
    /// </summary>
    public AnonymizerSettings Settings { get; }

    /// <summary>
    /// The immutable configuration options.
    /// </summary>
    public AnonymizerOptions Options { get; }

    /// <summary>
    /// Non-fatal warnings generated during processing.
    /// </summary>
    public List<string> Warnings { get; } = [];

    /// <summary>
    /// Tracks counts of each operation type applied.
    /// </summary>
    public Dictionary<string, int> OperationCounts { get; } = [];

    /// <summary>
    /// Tracks which security labels should be applied based on operations performed.
    /// </summary>
    public AppliedSecurityLabels SecurityLabels { get; set; } = new();

    /// <summary>
    /// Tracks visited node locations to prevent infinite recursion.
    /// Uses Location strings since IElement instances are not stable across calls.
    /// </summary>
    public HashSet<string> VisitedNodes { get; } = [];

    /// <summary>
    /// Rules that matched the current resource, populated by RuleMatchingMiddleware.
    /// </summary>
    public List<MatchedRule> MatchedRules { get; } = [];

    /// <summary>
    /// Creates a new anonymizer context.
    /// </summary>
    /// <param name="resource">The resource to anonymize.</param>
    /// <param name="element">The root element.</param>
    /// <param name="schema">The FHIR schema provider.</param>
    /// <param name="settings">Per-request settings.</param>
    /// <param name="options">Configuration options.</param>
    public AnonymizerContext(
        ResourceJsonNode resource,
        IElement element,
        IFhirSchemaProvider schema,
        AnonymizerSettings settings,
        AnonymizerOptions options)
    {
        Resource = resource;
        Element = element;
        Schema = schema;
        Settings = settings;
        Options = options;
        _stopwatch = Stopwatch.StartNew();
    }

    /// <summary>
    /// Increments the count for a specific operation type.
    /// </summary>
    /// <param name="operationType">The operation type (e.g., "REDACT", "DATESHIFT").</param>
    public void IncrementOperationCount(string operationType)
    {
        var key = operationType.ToUpperInvariant();
        OperationCounts.TryGetValue(key, out var count);
        OperationCounts[key] = count + 1;
    }

    /// <summary>
    /// Adds a warning message to the context.
    /// </summary>
    /// <param name="warning">The warning message.</param>
    public void AddWarning(string warning)
    {
        Warnings.Add(warning);
    }

    /// <summary>
    /// Builds the final anonymization result from the context state.
    /// </summary>
    /// <returns>The anonymization result.</returns>
    public AnonymizationResult BuildResult()
    {
        _stopwatch.Stop();

        var options = new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = Settings.IsPrettyOutput
        };
        var json = Resource.MutableNode.ToJsonString(options);

        var nodesProcessed = OperationCounts.Values.Sum();

        return new AnonymizationResult
        {
            AnonymizedJson = json,
            Metrics = new ProcessingMetrics
            {
                NodesProcessed = nodesProcessed,
                Duration = _stopwatch.Elapsed,
                OperationCounts = OperationCounts.ToImmutableDictionary()
            },
            Warnings = [.. Warnings],
            AppliedLabels = SecurityLabels
        };
    }
}

/// <summary>
/// Represents a matched rule with the elements it applies to.
/// </summary>
public sealed record MatchedRule
{
    /// <summary>
    /// The FHIRPath rule configuration.
    /// </summary>
    public required FhirPathRule Rule { get; init; }

    /// <summary>
    /// Elements matched by the FHIRPath expression.
    /// </summary>
    public required IReadOnlyList<IElement> MatchedElements { get; init; }
}
