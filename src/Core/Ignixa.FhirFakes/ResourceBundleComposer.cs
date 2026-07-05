// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.Serialization.Models;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.FhirFakes;

/// <summary>
/// Composes an ordered set of resources into a FHIR transaction or batch Bundle. Shared by
/// <see cref="Scenarios.ScenarioContext"/> and the workflow CLI so both emit identically-shaped bundles.
/// </summary>
public static class ResourceBundleComposer
{
    /// <summary>
    /// Creates a transaction Bundle: each entry uses a client-assigned <c>urn:uuid</c> fullUrl and a
    /// POST request, so the server assigns server ids and resolves cross-references.
    /// </summary>
    public static BundleJsonNode ToTransactionBundle(IEnumerable<ResourceJsonNode> resources) =>
        Compose(resources, "transaction", CreateTransactionEntry);

    /// <summary>
    /// Creates a batch Bundle: each entry uses a resolved <c>ResourceType/id</c> fullUrl and a PUT
    /// request, suitable when the resources already carry their final ids.
    /// </summary>
    public static BundleJsonNode ToBatchBundle(IEnumerable<ResourceJsonNode> resources) =>
        Compose(resources, "batch", CreateBatchEntry);

    private static BundleJsonNode Compose(
        IEnumerable<ResourceJsonNode> resources,
        string bundleType,
        Func<ResourceJsonNode, JsonObject> entryFactory)
    {
        ArgumentNullException.ThrowIfNull(resources);

        var entries = new JsonArray();
        var index = 0;
        foreach (var resource in resources)
        {
            if (resource is null)
            {
                throw new ArgumentException($"Resource at index {index} is null and cannot be added to a {bundleType} bundle.", nameof(resources));
            }

            if (string.IsNullOrEmpty(resource.ResourceType))
            {
                throw new ArgumentException($"Resource at index {index} has no resourceType and cannot be added to a {bundleType} bundle.", nameof(resources));
            }

            if (string.IsNullOrEmpty(resource.Id))
            {
                throw new ArgumentException($"{resource.ResourceType} resource at index {index} has no id and cannot be added to a {bundleType} bundle.", nameof(resources));
            }

            entries.Add(entryFactory(resource));
            index++;
        }

        var bundleNode = new JsonObject
        {
            ["resourceType"] = "Bundle",
            ["id"] = Guid.NewGuid().ToString(),
            ["type"] = bundleType,
            ["entry"] = entries,
        };

        return new BundleJsonNode(bundleNode);
    }

    private static JsonObject CreateTransactionEntry(ResourceJsonNode resource) => new()
    {
        ["fullUrl"] = $"urn:uuid:{resource.Id}",
        ["resource"] = resource.MutableNode.DeepClone(),
        ["request"] = new JsonObject
        {
            ["method"] = "POST",
            ["url"] = resource.ResourceType,
        },
    };

    private static JsonObject CreateBatchEntry(ResourceJsonNode resource) => new()
    {
        ["fullUrl"] = $"{resource.ResourceType}/{resource.Id}",
        ["resource"] = resource.MutableNode.DeepClone(),
        ["request"] = new JsonObject
        {
            ["method"] = "PUT",
            ["url"] = $"{resource.ResourceType}/{resource.Id}",
        },
    };
}
