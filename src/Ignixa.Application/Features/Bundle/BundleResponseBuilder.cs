// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Extensions.Logging;
using Ignixa.Application.Features.History;
using Ignixa.Domain.Models;
using Ignixa.SourceNodeSerialization.Models;
using Ignixa.SourceNodeSerialization.SourceNodes;

namespace Ignixa.Application.Features.Bundle;

/// <summary>
/// Builds FHIR response bundles from bundle entry responses.
/// Converts execution results into FHIR Bundle.entry.response format.
/// </summary>
public class BundleResponseBuilder
{
    private readonly ILogger<BundleResponseBuilder> _logger;

    public BundleResponseBuilder(ILogger<BundleResponseBuilder> logger)
    {
        _logger = EnsureArg.IsNotNull(logger, nameof(logger));
    }

    /// <summary>
    /// Builds a FHIR response bundle from entry responses.
    /// </summary>
    /// <param name="responses">List of bundle entry responses.</param>
    /// <param name="bundleType">Bundle type (transaction or batch).</param>
    /// <returns>FHIR Bundle with response entries.</returns>
    public BundleJsonNode BuildResponse(
        IReadOnlyList<BundleEntryResponse> responses,
        BundleType bundleType)
    {
        EnsureArg.IsNotNull(responses, nameof(responses));

        _logger.LogDebug("Building {Type} response bundle with {Count} entries", bundleType, responses.Count);

        var responseType = bundleType == BundleType.Transaction
            ? BundleJsonNode.BundleType.TransactionResponse
            : BundleJsonNode.BundleType.BatchResponse;

        var bundle = new BundleJsonNode
        {
            Type = responseType
        };

        foreach (var response in responses)
        {
            var entryComponent = BuildEntryComponent(response);
            bundle.AddEntry(entryComponent);
        }

        _logger.LogDebug("Successfully built {Type} response bundle", bundleType);

        return bundle;
    }

    private BundleComponentJsonNode BuildEntryComponent(BundleEntryResponse response)
    {
        var entry = new BundleComponentJsonNode
        {
            Response = new BundleComponentResponseJsonNode
            {
                Status = response.Status ?? response.StatusCode.ToString()
            }
        };

        // Add Location header for successful creates/updates
        if (!string.IsNullOrEmpty(response.Location))
        {
            entry.Response.Location = response.Location;
        }

        // Add ETag header for successful operations
        if (!string.IsNullOrEmpty(response.ETag))
        {
            entry.Response.Etag = response.ETag;
        }

        // Add LastModified header for successful operations
        if (response.LastModified.HasValue)
        {
            entry.Response.LastModified = response.LastModified.Value;
        }

        // Add resource to entry for GET and successful POST/PUT (201/200)
        if (!string.IsNullOrEmpty(response.ResourceJson) &&
            (response.StatusCode == 200 || response.StatusCode == 201))
        {
            try
            {
                // Parse JSON string back to ResourceJsonNode
                var resource = ResourceJsonNode.Parse(response.ResourceJson);
                entry.Resource = resource;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to parse resource JSON for response status {Status}",
                    response.StatusCode);

                // Add OperationOutcome if parsing fails
                var outcome = new OperationOutcomeJsonNode();
                outcome.SetIssues(new List<OperationOutcomeJsonNode.IssueComponent>
                {
                    new OperationOutcomeJsonNode.IssueComponent
                    {
                        Severity = OperationOutcomeJsonNode.IssueSeverity.Warning,
                        Code = OperationOutcomeJsonNode.IssueType.Invalid,
                        Diagnostics = $"Failed to parse resource JSON: {ex.Message}"
                    }
                });
                entry.Response.Outcome = outcome;
            }
        }

        return entry;
    }
}
