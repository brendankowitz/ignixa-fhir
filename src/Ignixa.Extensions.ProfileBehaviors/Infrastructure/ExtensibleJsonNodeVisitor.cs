// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using EnsureThat;
using Ignixa.Abstractions;
using Ignixa.Extensions.ProfileBehaviors.Abstractions;

namespace Ignixa.Extensions.ProfileBehaviors.Infrastructure;

/// <summary>
/// Visits a JsonNode tree representing a FHIR resource, invoking visitor callbacks for each property.
/// Enables extensible transformations: filtering, mutation, injection of missing elements.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Design</strong>: Unlike streaming serializers, this works on mutable JsonNode trees.
/// Useful for pre-processing resources before validation (e.g., injecting data-absent-reason).
/// </para>
/// <para>
/// <strong>Two-Phase Visitation</strong>:
/// 1. Visit existing properties → VisitProperty()
/// 2. Detect missing mandatory properties → VisitMissingProperty()
/// </para>
/// <para>
/// <strong>Example Usage</strong>:
/// <code>
/// var visitor = new ExtensibleJsonNodeVisitor(schemaProvider, propertyVisitor);
/// visitor.Visit(resourceNode, "Patient", FhirSpecification.R4);
/// </code>
/// </para>
/// </remarks>
public sealed class ExtensibleJsonNodeVisitor
{
    private readonly IStructureDefinitionSummaryProvider _schemaProvider;
    private readonly IResourcePropertyVisitor _visitor;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExtensibleJsonNodeVisitor"/> class.
    /// </summary>
    /// <param name="schemaProvider">Provider for FHIR structure definitions.</param>
    /// <param name="visitor">Visitor to invoke for each property.</param>
    public ExtensibleJsonNodeVisitor(
        IStructureDefinitionSummaryProvider schemaProvider,
        IResourcePropertyVisitor visitor)
    {
        _schemaProvider = EnsureArg.IsNotNull(schemaProvider, nameof(schemaProvider));
        _visitor = EnsureArg.IsNotNull(visitor, nameof(visitor));
    }

    /// <summary>
    /// Visits a resource JsonNode, invoking visitor for each property.
    /// Mutates the JsonNode in-place based on visitor results (Include/Skip/Mutate/Inject).
    /// </summary>
    /// <param name="resourceNode">The root resource JsonNode.</param>
    /// <param name="resourceType">The FHIR resource type (e.g., "Patient").</param>
    /// <param name="fhirVersion">The FHIR version.</param>
    /// <param name="maxDepth">Maximum depth to visit (0 = root only, -1 = unlimited).</param>
    /// <param name="options">Custom options for visitor.</param>
    public void Visit(
        JsonNode resourceNode,
        string resourceType,
        FhirSpecification fhirVersion,
        int maxDepth = -1,
        Dictionary<string, object>? options = null)
    {
        EnsureArg.IsNotNull(resourceNode, nameof(resourceNode));
        EnsureArg.IsNotNullOrEmpty(resourceType, nameof(resourceType));

        var context = new VisitorContext
        {
            ResourceType = resourceType,
            FhirVersion = fhirVersion,
            MaxDepth = maxDepth,
            Options = options ?? new Dictionary<string, object>()
        };

        // Get schema for this resource type
        var schema = _schemaProvider.Provide(resourceType);
        if (schema == null)
        {
            return; // No schema available, cannot walk
        }

        // Build element metadata lookup
        var elementMetadata = schema.GetElements()
            .ToDictionary(e => e.ElementName, ElementMetadata.FromElementDefinition);

        // Visit the resource
        VisitObject(resourceNode.AsObject(), elementMetadata, context, depth: 0);

        // Check for missing mandatory elements and potentially inject them
        InjectMissingMandatoryElements(resourceNode.AsObject(), elementMetadata, context);
    }

    /// <summary>
    /// Visits a JsonObject, processing existing properties.
    /// </summary>
    private void VisitObject(
        JsonObject obj,
        Dictionary<string, ElementMetadata> elementMetadata,
        VisitorContext context,
        int depth)
    {
        // Check depth limit
        if (context.MaxDepth >= 0 && depth > context.MaxDepth)
        {
            return;
        }

        // Get list of properties to walk (copy to avoid modification during iteration)
        var propertiesToWalk = obj.Select(kvp => kvp.Key).ToList();

        foreach (var propertyName in propertiesToWalk)
        {
            var propertyValue = obj[propertyName];

            // Get element metadata
            var metadata = elementMetadata.GetValueOrDefault(propertyName);

            // Visit property
            var result = _visitor.VisitProperty(propertyName, metadata, depth, context);

            switch (result.Action)
            {
                case PropertyAction.Include:
                    // Keep property as-is, but recurse if needed
                    RecurseIfNeeded(propertyValue, context, depth);
                    break;

                case PropertyAction.Skip:
                    // Remove property
                    obj.Remove(propertyName);
                    break;

                case PropertyAction.Mutate:
                    // Apply mutation function
                    if (result.MutationFunc != null)
                    {
                        var mutated = result.MutationFunc(propertyValue);
                        if (mutated != null)
                        {
                            obj[propertyName] = mutated;
                            RecurseIfNeeded(mutated, context, depth);
                        }
                        else
                        {
                            obj.Remove(propertyName);
                        }
                    }
                    break;

                case PropertyAction.Inject:
                    // Inject action not valid for existing properties
                    break;
            }
        }
    }

    /// <summary>
    /// Recurses into nested objects/arrays if needed.
    /// </summary>
    private void RecurseIfNeeded(JsonNode? node, VisitorContext context, int depth)
    {
        if (node == null)
        {
            return;
        }

        // Check depth limit for recursion
        if (context.MaxDepth >= 0 && depth + 1 > context.MaxDepth)
        {
            return;
        }

        if (node is JsonObject nestedObj)
        {
            // For nested objects, we don't have schema metadata (limitation for now)
            // Just recurse without metadata
            var emptyMetadata = new Dictionary<string, ElementMetadata>();
            VisitObject(nestedObj, emptyMetadata, context, depth + 1);
        }
        else if (node is JsonArray array)
        {
            foreach (var item in array)
            {
                RecurseIfNeeded(item, context, depth + 1);
            }
        }
    }

    /// <summary>
    /// Checks for missing mandatory elements and potentially injects them.
    /// Only processes root-level elements (depth = 0) to avoid complexity.
    /// </summary>
    private void InjectMissingMandatoryElements(
        JsonObject obj,
        Dictionary<string, ElementMetadata> elementMetadata,
        VisitorContext context)
    {
        // Find all mandatory elements (IsRequired = true)
        var mandatoryElements = elementMetadata.Values.Where(e => e.IsRequired).ToList();

        foreach (var element in mandatoryElements)
        {
            // Check if element exists
            if (!obj.ContainsKey(element.ElementName))
            {
                // Element is missing - visit to decide what to do
                var result = _visitor.VisitMissingProperty(element.ElementName, element, depth: 0, context);

                if (result.Action == PropertyAction.Inject && result.InjectionFunc != null)
                {
                    // Inject the missing property
                    var injectedValue = result.InjectionFunc();
                    obj[element.ElementName] = injectedValue;
                }
            }
        }
    }
}
