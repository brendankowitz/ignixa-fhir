// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ignixa.Application.Features.Metadata.Models;
using Ignixa.FhirFakes;
using Ignixa.Serialization;
using Ignixa.Serialization.Models;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;

namespace Ignixa.Api.E2ETests._Infrastructure.Harness;

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
    /// Checks if a system-level operation is supported.
    /// Uses cached capability statement - synchronous, no network call.
    /// </summary>
    public bool SupportsSystemOperation(string operationName)
    {
        if (_capability.Rest is null || _capability.Rest.Count == 0)
        {
            return false;
        }

        var rest = _capability.Rest[0];
        if (rest.Operation is null)
        {
            return false;
        }

        var normalizedOperationName = NormalizeOperationName(operationName);
        return rest.Operation.Any(op => NormalizeOperationName(op.Name) == normalizedOperationName);
    }

    /// <summary>
    /// Checks if a resource-level operation is supported for a given resource type.
    /// Uses cached capability statement - synchronous, no network call.
    /// </summary>
    public bool SupportsResourceOperation(string resourceType, string operationName)
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
        if (resource is null || resource.Operation is null)
        {
            return false;
        }

        var normalizedOperationName = NormalizeOperationName(operationName);
        return resource.Operation.Any(op => NormalizeOperationName(op.Name) == normalizedOperationName);
    }

    /// <summary>
    /// Checks if an operation is supported either at the system level or for any resource.
    /// </summary>
    public bool SupportsOperationAnywhere(string operationName)
    {
        if (SupportsSystemOperation(operationName))
        {
            return true;
        }

        if (_capability.Rest is null || _capability.Rest.Count == 0)
        {
            return false;
        }

        var rest = _capability.Rest[0];
        if (rest.Resource is null)
        {
            return false;
        }

        return rest.Resource.Any(resource => SupportsResourceOperation(resource.Type ?? string.Empty, operationName));
    }

    private static string NormalizeOperationName(string? operationName)
    {
        if (string.IsNullOrWhiteSpace(operationName))
        {
            return string.Empty;
        }

        return operationName.Trim().TrimStart('$');
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

        // Create transaction bundle (supports PUT with resolved references)
        var bundle = new BundleJsonNode
        {
            Type = BundleJsonNode.BundleType.Transaction
        };

        // Add entries for each resource
        foreach (var resource in resources)
        {
            var entry = new BundleComponentJsonNode
            {
                Resource = resource,
                Request = new BundleComponentRequestJsonNode
                {
                    Method = "PUT",
                    Url = $"{resource.ResourceType}/{resource.Id}"
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

        // Extract only match entries from bundle (exclude outcome entries like OperationOutcome)
        // In FHIR searchset bundles, search.mode indicates: "match", "include", or "outcome"
        return bundle.Entry
            .Where(e => e.Resource is not null && e.Search?.Mode != "outcome")
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

    /// <summary>
    /// Posts a resource with custom headers (for conditional create, If-Match, etc.).
    /// Returns the raw HttpResponseMessage for status code and header inspection.
    /// </summary>
    public async Task<HttpResponseMessage> PostResourceWithHeadersAsync(
        ResourceJsonNode resource,
        Dictionary<string, string>? headers = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resource);

        var resourceType = resource.ResourceType;
        var json = resource.MutableNode.ToJsonString();

        var request = new HttpRequestMessage(HttpMethod.Post, $"/{resourceType}")
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/fhir+json")
        };

        if (headers is not null)
        {
            foreach (var header in headers)
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return await _client.SendAsync(request, cancellationToken);
    }

    /// <summary>
    /// Parses a resource from an HttpResponseMessage.
    /// </summary>
    public async Task<ResourceJsonNode> ParseResourceResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(response);

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonSourceNodeFactory.Parse<ResourceJsonNode>(responseJson);
    }

    /// <summary>
    /// Puts a resource with query string (for conditional update operations).
    /// Returns the raw HttpResponseMessage for status code and header inspection.
    /// </summary>
    /// <param name="resource">The resource to update.</param>
    /// <param name="queryString">Query string with search parameters (e.g., "identifier=system|value").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>HttpResponseMessage with status code, Location header (if created), and resource body.</returns>
    /// <remarks>
    /// This method supports conditional update as defined in FHIR R4 Section 3.1.0.6.
    /// Expected response codes:
    /// - 200 OK: One match found, resource updated successfully
    /// - 201 Created: No matches found, new resource created
    /// - 412 Precondition Failed: Multiple matches found
    /// - 400 Bad Request: No search criteria or invalid resource type (e.g., Bundle)
    /// </remarks>
    public async Task<HttpResponseMessage> PutResourceWithQueryAsync(
        ResourceJsonNode resource,
        string queryString,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resource);

        var resourceType = resource.ResourceType;
        var json = resource.MutableNode.ToJsonString();

        var url = string.IsNullOrEmpty(queryString)
            ? $"/{resourceType}"
            : $"/{resourceType}?{queryString}";

        var request = new HttpRequestMessage(HttpMethod.Put, url)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/fhir+json")
        };

        return await _client.SendAsync(request, cancellationToken);
    }

    /// <summary>
    /// Executes a conditional delete via DELETE request with query string.
    /// Returns the raw HttpResponseMessage for status code and response inspection.
    /// </summary>
    /// <param name="resourceType">The resource type to delete (e.g., "Patient").</param>
    /// <param name="queryString">Query string with search parameters (e.g., "identifier=system|value&_count=10").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>HttpResponseMessage with status code and optional body.</returns>
    public async Task<HttpResponseMessage> DeleteWithQueryAsync(
        string resourceType,
        string queryString,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resourceType);

        var url = string.IsNullOrEmpty(queryString)
            ? $"/{resourceType}"
            : $"/{resourceType}?{queryString}";

        var request = new HttpRequestMessage(HttpMethod.Delete, url);
        return await _client.SendAsync(request, cancellationToken);
    }

    /// <summary>
    /// Executes a conditional patch via PATCH request with query string.
    /// Returns the raw HttpResponseMessage for status code and response inspection.
    /// </summary>
    /// <param name="resourceType">The resource type to patch (e.g., "Patient").</param>
    /// <param name="queryString">Query string with search parameters (e.g., "identifier=system|value").</param>
    /// <param name="patchDocument">The FHIRPath Patch document (Parameters resource).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>HttpResponseMessage with status code and patched resource body (if successful).</returns>
    /// <remarks>
    /// This method supports FHIRPath Patch (Parameters resource) as defined in FHIR R4 Section 3.1.0.7.1.
    /// JSON Patch (RFC 6902) is NOT supported by this server.
    ///
    /// Expected response codes:
    /// - 200 OK: One match found, resource patched successfully
    /// - 404 Not Found: No matches found for search criteria
    /// - 412 Precondition Failed: Multiple matches or no search criteria
    /// - 400 Bad Request: Invalid resource type (e.g., Bundle)
    /// </remarks>
    public async Task<HttpResponseMessage> PatchWithQueryAsync(
        string resourceType,
        string queryString,
        ResourceJsonNode patchDocument,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resourceType);
        ArgumentNullException.ThrowIfNull(patchDocument);

        var url = string.IsNullOrEmpty(queryString)
            ? $"/{resourceType}"
            : $"/{resourceType}?{queryString}";

        var json = patchDocument.MutableNode.ToJsonString();

        var request = new HttpRequestMessage(new HttpMethod("PATCH"), url)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/fhir+json")
        };

        return await _client.SendAsync(request, cancellationToken);
    }
}
