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
    public RequestOptions Settings { get; }

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
        RequestOptions settings,
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

        if (OperationCounts.Count > 0)
        {
            AddSecurityTagsToResource();
        }

        var options = new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = Settings.IsPrettyOutput
        };
        var json = Resource.MutableNode.ToJsonString(options);

        var nodesProcessed = OperationCounts.Values.Sum();

        return new AnonymizationResult
        {
            Resource = Resource,
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

    /// <summary>
    /// Adds security tags to the resource's meta.security array based on operations performed.
    /// Inserts meta after the 'id' property to maintain FHIR property ordering.
    /// </summary>
    private void AddSecurityTagsToResource()
    {
        var mutableNode = Resource.MutableNode;

        // Check if meta already exists
        bool metaExists = mutableNode["meta"] is System.Text.Json.Nodes.JsonObject existingMetaObj;
        System.Text.Json.Nodes.JsonObject metaObj;

        if (metaExists)
        {
            metaObj = (System.Text.Json.Nodes.JsonObject)mutableNode["meta"]!;
        }
        else
        {
            // Create new meta object and insert it after 'id' property
            metaObj = new System.Text.Json.Nodes.JsonObject();
            InsertMetaAfterIdProperty(mutableNode, metaObj);
        }

        // Ensure security array exists
        if (metaObj["security"] is not System.Text.Json.Nodes.JsonArray securityArr)
        {
            securityArr = new System.Text.Json.Nodes.JsonArray();
            metaObj["security"] = securityArr;
        }

        // Add labels based on applied security labels
        AddSecurityLabelIfNeeded(securityArr, SecurityLabels.IsRedacted, Models.SecurityLabels.REDACT);
        AddSecurityLabelIfNeeded(securityArr, SecurityLabels.IsAbstracted, Models.SecurityLabels.ABSTRED);
        AddSecurityLabelIfNeeded(securityArr, SecurityLabels.IsCryptoHashed, Models.SecurityLabels.CRYTOHASH);
        AddSecurityLabelIfNeeded(securityArr, SecurityLabels.IsEncrypted, Models.SecurityLabels.MASKED);
        AddSecurityLabelIfNeeded(securityArr, SecurityLabels.IsPerturbed, Models.SecurityLabels.PERTURBED);
        AddSecurityLabelIfNeeded(securityArr, SecurityLabels.IsSubstituted, Models.SecurityLabels.SUBSTITUTED);
        AddSecurityLabelIfNeeded(securityArr, SecurityLabels.IsGeneralized, Models.SecurityLabels.GENERALIZED);

        Resource.InvalidateCaches();
    }

    /// <summary>
    /// Inserts the meta property after the 'id' property to maintain FHIR property ordering.
    /// Per FHIR spec, the standard order is: resourceType, id, meta, implicitRules, language, then other properties.
    /// </summary>
    private static void InsertMetaAfterIdProperty(System.Text.Json.Nodes.JsonObject mutableNode, System.Text.Json.Nodes.JsonObject metaObj)
    {
        // Collect all current properties
        var properties = mutableNode.ToList();

        // Clear the object
        mutableNode.Clear();

        // Re-add properties in the correct order
        bool metaInserted = false;
        foreach (var kvp in properties)
        {
            mutableNode[kvp.Key] = kvp.Value;

            // Insert meta after 'id'
            if (kvp.Key == "id" && !metaInserted)
            {
                mutableNode["meta"] = metaObj;
                metaInserted = true;
            }
        }

        // If there was no 'id' property, insert after 'resourceType'
        if (!metaInserted)
        {
            // Rebuild to put meta right after resourceType
            var allProps = mutableNode.ToList();
            mutableNode.Clear();
            foreach (var kvp in allProps)
            {
                mutableNode[kvp.Key] = kvp.Value;
                if (kvp.Key == "resourceType")
                {
                    mutableNode["meta"] = metaObj;
                    metaInserted = true;
                }
            }

            if (!metaInserted)
            {
                mutableNode["meta"] = metaObj;
            }
        }
    }

    /// <summary>
    /// Adds a security label to the security array if the condition is true and the label doesn't already exist.
    /// </summary>
    private static void AddSecurityLabelIfNeeded(System.Text.Json.Nodes.JsonArray securityArr, bool condition, Models.SecurityLabel label)
    {
        if (!condition)
        {
            return;
        }

        // Check if the label already exists
        foreach (var item in securityArr)
        {
            if (item is System.Text.Json.Nodes.JsonObject obj &&
                obj["code"]?.GetValue<string>() is string code &&
                string.Equals(code, label.Code, StringComparison.InvariantCultureIgnoreCase))
            {
                return;
            }
        }

        securityArr.Add(label.ToJsonObject());
    }
}
