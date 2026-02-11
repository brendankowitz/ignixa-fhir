// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Text.Json.Nodes;
using EnsureThat;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;
using Microsoft.Extensions.Logging;
using Ignixa.Anonymizer.AnonymizerConfigurations;
using Ignixa.Anonymizer.Exceptions;
using Ignixa.Anonymizer.Extensions;
using Ignixa.Anonymizer.Models;

namespace Ignixa.Anonymizer.Processors;

public class ResourceProcessor
{
    private readonly AnonymizationFhirPathRule[] _rules;
    private readonly Dictionary<string, IAnonymizerProcessor> _processors;
    private readonly ILogger _logger = AnonymizerLogging.CreateLogger<ResourceProcessor>();

    private readonly HashSet<string> _visitedNodes = [];

    public ResourceProcessor(AnonymizationFhirPathRule[] rules, Dictionary<string, IAnonymizerProcessor> processors)
    {
        EnsureArg.IsNotNull(rules, nameof(rules));
        EnsureArg.IsNotNull(processors, nameof(processors));

        _rules = rules;
        _processors = processors;
    }

    public ProcessResult Process(ResourceJsonNode resource, IElement node, ProcessContext? context = null, Dictionary<string, object>? settings = null)
    {
        EnsureArg.IsNotNull(resource, nameof(resource));
        EnsureArg.IsNotNull(node, nameof(node));

        var result = new ProcessResult();
        var resourceRules = GetRulesByType(node.InstanceType);

        foreach (var rule in resourceRules)
        {
            var ruleResult = new ProcessResult();
            var method = rule.Method.ToUpperInvariant();
            var ruleContext = new ProcessContext
            {
                VisitedNodes = _visitedNodes,
                ResourceId = node.GetNodeId()
            };

            if (!_processors.ContainsKey(method))
            {
                throw new AnonymizerConfigurationException($"Anonymization method {method} not supported.");
            }

            var matchNodes = GetMatchNodes(rule, node);

            foreach (var matchNode in matchNodes)
            {
                ruleResult.Update(ProcessNodeRecursive(resource, matchNode, _processors[method], ruleContext, rule.RuleSettings));
            }

            LogProcessResult(node, rule, ruleResult);

            result.Update(ruleResult);
        }

        return result;
    }

    public void AddSecurityTag(ResourceJsonNode resource, IElement node, ProcessResult result)
    {
        if (node is null || result.ProcessRecords.Count == 0)
        {
            return;
        }

        // Get the mutable JsonObject for this specific node (handles nested resources in bundles)
        var mutableNode = GetMutableNodeForElement(resource, node);
        if (mutableNode is null)
        {
            return;
        }

        // Check if meta already exists
        bool metaExists = mutableNode["meta"] is JsonObject existingMetaObj;
        JsonObject metaObj;

        if (metaExists)
        {
            metaObj = (JsonObject)mutableNode["meta"]!;
        }
        else
        {
            // Create new meta object and insert it after 'id' property
            metaObj = new JsonObject();
            InsertMetaAfterIdProperty(mutableNode, metaObj);
        }

        // Ensure security array exists
        if (metaObj["security"] is not JsonArray securityArr)
        {
            securityArr = new JsonArray();
            metaObj["security"] = securityArr;
        }

        AddSecurityLabelIfNeeded(securityArr, result.IsRedacted, SecurityLabels.REDACT);
        AddSecurityLabelIfNeeded(securityArr, result.IsAbstracted, SecurityLabels.ABSTRED);
        AddSecurityLabelIfNeeded(securityArr, result.IsCryptoHashed, SecurityLabels.CRYTOHASH);
        AddSecurityLabelIfNeeded(securityArr, result.IsEncrypted, SecurityLabels.MASKED);
        AddSecurityLabelIfNeeded(securityArr, result.IsPerturbed, SecurityLabels.PERTURBED);
        AddSecurityLabelIfNeeded(securityArr, result.IsSubstituted, SecurityLabels.SUBSTITUTED);
        AddSecurityLabelIfNeeded(securityArr, result.IsGeneralized, SecurityLabels.GENERALIZED);

        resource.InvalidateCaches();
    }

    /// <summary>
    /// Gets the mutable JsonObject for a specific IElement node, handling nested resources in bundles.
    /// For the root resource, returns resource.MutableNode.
    /// For nested resources (e.g., Patient in Bundle.entry.resource), navigates the JSON tree using the node's location.
    /// </summary>
    private static JsonObject? GetMutableNodeForElement(ResourceJsonNode resource, IElement node)
    {
        var rootMutable = resource.MutableNode;
        var location = node.Location;

        // If this is the root resource, return the root mutable node
        if (location == rootMutable["resourceType"]?.GetValue<string>())
        {
            return rootMutable;
        }

        // Parse the location path (e.g., "Bundle.entry[0].resource")
        // and navigate to the corresponding JsonObject
        var parts = location.Split('.');
        JsonNode? current = rootMutable;

        // Skip the first part if it matches the root resource type
        int startIndex = (parts.Length > 0 && parts[0] == rootMutable["resourceType"]?.GetValue<string>()) ? 1 : 0;

        for (int i = startIndex; i < parts.Length; i++)
        {
            var part = parts[i];

            // Handle array indexing (e.g., "entry[0]")
            if (part.Contains('['))
            {
                var arrayName = part.Substring(0, part.IndexOf('['));
                var indexStr = part.Substring(part.IndexOf('[') + 1, part.IndexOf(']') - part.IndexOf('[') - 1);
                if (int.TryParse(indexStr, out int index))
                {
                    if (current is JsonObject obj && obj[arrayName] is JsonArray arr && index < arr.Count)
                    {
                        current = arr[index];
                    }
                    else
                    {
                        return null;
                    }
                }
                else
                {
                    return null;
                }
            }
            else
            {
                // Simple property access
                if (current is JsonObject obj && obj.ContainsKey(part))
                {
                    current = obj[part];
                }
                else
                {
                    return null;
                }
            }
        }

        return current as JsonObject;
    }

    /// <summary>
    /// Inserts the meta property after the 'id' property in the JsonObject to maintain FHIR property ordering.
    /// Per FHIR spec, the standard order is: resourceType, id, meta, implicitRules, language, then other properties.
    /// </summary>
    private static void InsertMetaAfterIdProperty(JsonObject mutableNode, JsonObject metaObj)
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

    private static void AddSecurityLabelIfNeeded(JsonArray securityArr, bool condition, SecurityLabels.SecurityLabel label)
    {
        if (!condition)
        {
            return;
        }

        // Check if the label already exists
        foreach (var item in securityArr)
        {
            if (item is JsonObject obj &&
                obj["code"]?.GetValue<string>() is string code &&
                string.Equals(code, label.Code, StringComparison.InvariantCultureIgnoreCase))
            {
                return;
            }
        }

        securityArr.Add(label.ToJsonObject());
    }


    private IEnumerable<AnonymizationFhirPathRule> GetRulesByType(string typeString)
    {
        return _rules.Where(r => r.ResourceType.Equals(typeString)
                                 || string.IsNullOrEmpty(r.ResourceType)
                                 || string.Equals(Constants.GeneralResourceType, r.ResourceType)
                                 || string.Equals(Constants.GeneralDomainResourceType, r.ResourceType));
    }

    private IEnumerable<IElement> GetMatchNodes(AnonymizationFhirPathRule rule, IElement node)
    {
        // For resource-type rules (e.g., "Patient"), return the root node
        // Otherwise, evaluate the FHIRPath expression
        return rule.IsResourceTypeRule
            ? [node]
            : node.Select(rule.Expression);
    }

    private void LogProcessResult(IElement node, AnonymizationFhirPathRule rule, ProcessResult resultOnRule)
    {
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            string resourceId = node.GetNodeId();
            foreach (var processRecord in resultOnRule.ProcessRecords)
            {
                foreach (var matchNode in processRecord.Value)
                {
                    _logger.LogDebug("[{ResourceId}]: Rule '{Path}' matches '{Location}' and perform operation '{Operation}'",
                        resourceId, rule.Path, matchNode.Location, processRecord.Key);
                }
            }
        }
    }

    public ProcessResult ProcessNodeRecursive(ResourceJsonNode resource, IElement node, IAnonymizerProcessor processor, ProcessContext context, Dictionary<string, object>? settings)
    {
        var result = new ProcessResult();
        if (_visitedNodes.Contains(node.Location))
        {
            return result;
        }

        try
        {
            result = processor.Process(resource, node, context, settings);
        }
        catch (AnonymizerProcessingException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new AnonymizerProcessingException(
                $"Processing failed on node '{node.Location}' with type '{node.InstanceType}'.", ex);
        }

        _visitedNodes.Add(node.Location);

        foreach (var child in node.Children())
        {
            if (child.IsFhirResource())
            {
                continue;
            }

            result.Update(ProcessNodeRecursive(resource, child, processor, context, settings));
        }

        return result;
    }
}
