// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Nodes;
using Ignixa.Application.Features.Metadata.Models;
using Ignixa.FhirFakes;
using Ignixa.Serialization;
using Ignixa.Serialization.Models;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;

namespace Ignixa.Api.E2ETests.Infrastructure;

/// <summary>
/// Test harness for E2E search testing.
/// Provides helpers for resource creation, search execution, and capability checking.
/// </summary>
public sealed class SearchTestHarness
{
    private readonly HttpClient _client;
    private readonly IFhirSchemaProvider _schemaProvider;
    private readonly CapabilityStatementJsonNode _capability;

    public SearchTestHarness(
        HttpClient client,
        IFhirSchemaProvider schemaProvider,
        CapabilityStatementJsonNode capability)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _schemaProvider = schemaProvider ?? throw new ArgumentNullException(nameof(schemaProvider));
        _capability = capability ?? throw new ArgumentNullException(nameof(capability));
    }

    /// <summary>
    /// Creates a new faker instance configured for this harness's FHIR version.
    /// </summary>
    public SchemaBasedFhirResourceFaker CreateFaker()
    {
        return new SchemaBasedFhirResourceFaker(_schemaProvider);
    }

    /// <summary>
    /// Checks if a search parameter is supported for a given resource type.
    /// Uses cached capability statement - synchronous, no network call.
    /// </summary>
    public bool SupportsSearch(string resourceType, string parameterName)
    {
        if (_capability.Rest is null || _capability.Rest.Count == 0)
        {
            return false;
        }

        var rest = _capability.Rest[0];
        if (rest.Resource is null)
        {
            return false;
        }

        var resource = rest.Resource.FirstOrDefault(r => r.Type == resourceType);
        if (resource is null)
        {
            return false;
        }

        return resource.SearchParam?.Any(sp => sp.Name == parameterName) ?? false;
    }

    /// <summary>
    /// Creates a single resource on the server.
    /// </summary>
    public async Task<ResourceJsonNode> CreateResourceAsync(ResourceJsonNode resource, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resource);

        var resourceType = resource.ResourceType;
        var json = resource.MutableNode.ToJsonString();

        var response = await _client.PostAsync(
            $"/{resourceType}",
            new StringContent(json, System.Text.Encoding.UTF8, "application/fhir+json"),
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonSourceNodeFactory.Parse<ResourceJsonNode>(responseJson);
    }

    /// <summary>
    /// Updates a resource on the server using PUT.
    /// </summary>
    public async Task<ResourceJsonNode> UpdateResourceAsync(ResourceJsonNode resource, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resource);

        var resourceType = resource.ResourceType;
        var resourceId = resource.Id;

        if (string.IsNullOrEmpty(resourceId))
        {
            throw new ArgumentException("Resource must have an ID for update", nameof(resource));
        }

        var json = resource.MutableNode.ToJsonString();

        var response = await _client.PutAsync(
            $"/{resourceType}/{resourceId}",
            new StringContent(json, System.Text.Encoding.UTF8, "application/fhir+json"),
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonSourceNodeFactory.Parse<ResourceJsonNode>(responseJson);
    }

    /// <summary>
    /// Creates multiple resources on the server using a FHIR batch bundle for better performance.
    /// </summary>
    public async Task<ResourceJsonNode[]> CreateResourcesAsync(ResourceJsonNode[] resources, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resources);

        if (resources.Length == 0)
        {
            return [];
        }

        // Create batch bundle
        var bundle = new BundleJsonNode
        {
            Type = BundleJsonNode.BundleType.Batch
        };

        // Add entries for each resource
        foreach (var resource in resources)
        {
            var entry = new BundleComponentJsonNode
            {
                Resource = resource,
                Request = new BundleComponentRequestJsonNode
                {
                    Method = "POST",
                    Url = resource.ResourceType
                }
            };

            bundle.Entry.Add(entry);
        }

        // POST bundle to root
        var bundleJson = bundle.MutableNode.ToJsonString();
        var response = await _client.PostAsync(
            "/",
            new StringContent(bundleJson, System.Text.Encoding.UTF8, "application/fhir+json"),
            cancellationToken);

        response.EnsureSuccessStatusCode();

        // Parse response bundle
        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        var responseBundle = JsonSourceNodeFactory.Parse<BundleJsonNode>(responseJson);

        // Extract created resources from response entries
        return responseBundle.Entry
            .Where(e => e.Resource is not null)
            .Select(e => e.Resource!)
            .ToArray();
    }

    /// <summary>
    /// Executes a search and returns matching resources.
    /// </summary>
    public async Task<ResourceJsonNode[]> SearchAsync(string resourceType, string queryString, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resourceType);

        var url = string.IsNullOrEmpty(queryString)
            ? $"/{resourceType}"
            : $"/{resourceType}?{queryString}";

        var response = await _client.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        var bundle = JsonSourceNodeFactory.Parse<BundleJsonNode>(responseJson);

        // Extract resources from bundle entries
        return bundle.Entry
            .Where(e => e.Resource is not null)
            .Select(e => e.Resource!)
            .ToArray();
    }

    /// <summary>
    /// Executes a search and returns the full Bundle (for pagination/total tests).
    /// </summary>
    public async Task<BundleJsonNode> SearchBundleAsync(string resourceType, string queryString, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resourceType);

        var url = string.IsNullOrEmpty(queryString)
            ? $"/{resourceType}"
            : $"/{resourceType}?{queryString}";

        var response = await _client.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonSourceNodeFactory.Parse<BundleJsonNode>(responseJson);
    }

    /// <summary>
    /// Executes a system-level search (no resource type) and returns the full Bundle.
    /// Used for cross-resource-type searches with _type parameter.
    /// </summary>
    public async Task<BundleJsonNode> SearchSystemAsync(string queryString, CancellationToken cancellationToken = default)
    {
        var url = string.IsNullOrEmpty(queryString)
            ? "/"
            : $"/?{queryString}";

        var response = await _client.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonSourceNodeFactory.Parse<BundleJsonNode>(responseJson);
    }

    /// <summary>
    /// Executes a search via GET request to a URL (for following next links).
    /// </summary>
    public async Task<BundleJsonNode> GetBundleAsync(string url, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(url);

        var response = await _client.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonSourceNodeFactory.Parse<BundleJsonNode>(responseJson);
    }
}
