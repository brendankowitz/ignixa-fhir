// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;
using System.Text.Json.Nodes;
using Ignixa.Application.Infrastructure;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Search.Expressions;
using Ignixa.Search.Models;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.ValueSets.Normative;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.Operations.Features.MemberMatch;

/// <summary>
/// Default implementation of member matching strategy.
/// Uses identifier-based matching to find a unique patient.
///
/// Matching algorithm:
/// 1. Extract subscriber ID from Coverage.subscriberId
/// 2. Extract member ID from Patient.identifier
/// 3. Search for Patient with matching identifier
/// 4. Return unique match or error if no match/multiple matches found
/// </summary>
public class DefaultMemberMatchStrategy : IMemberMatchStrategy
{
    private readonly ISearchServiceFactory _searchServiceFactory;
    private readonly IFhirRequestContextAccessor _contextAccessor;
    private readonly ILogger<DefaultMemberMatchStrategy> _logger;

    public DefaultMemberMatchStrategy(
        ISearchServiceFactory searchServiceFactory,
        IFhirRequestContextAccessor contextAccessor,
        ILogger<DefaultMemberMatchStrategy> logger)
    {
        _searchServiceFactory = searchServiceFactory ?? throw new ArgumentNullException(nameof(searchServiceFactory));
        _contextAccessor = contextAccessor ?? throw new ArgumentNullException(nameof(contextAccessor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<MemberMatchResult> MatchAsync(
        ResourceJsonNode memberPatient,
        ResourceJsonNode coverageToMatch,
        ResourceJsonNode? coverageToLink,
        CancellationToken cancellationToken)
    {
        var context = _contextAccessor.RequestContext
            ?? throw new InvalidOperationException("FHIR request context not available");

        _logger.LogDebug("Executing member match with default strategy");

        // Extract identifiers from input resources
        var patientIdentifiers = ExtractIdentifiers(memberPatient);
        var subscriberId = ExtractSubscriberId(coverageToMatch);

        if (patientIdentifiers.Count == 0 && string.IsNullOrEmpty(subscriberId))
        {
            _logger.LogWarning("No identifiers found in MemberPatient or CoverageToMatch");
            return MemberMatchResult.NoMatch(
                "No identifiers provided. At least one identifier in MemberPatient or subscriberId in CoverageToMatch is required for matching.");
        }

        // Build search criteria
        var searchExpressions = new List<Expression>();

        // Add identifier search from Patient.identifier
        foreach (var identifier in patientIdentifiers)
        {
            var tokenExpression = BuildIdentifierExpression(identifier);
            if (tokenExpression != null)
            {
                searchExpressions.Add(tokenExpression);
            }
        }

        // Add identifier search from Coverage.subscriberId
        if (!string.IsNullOrEmpty(subscriberId))
        {
            var subscriberExpression = new SearchParameterExpression(
                new SearchParameterInfo("identifier", "identifier", SearchParamType.Token),
                new StringExpression(StringOperator.Equals, FieldName.TokenCode, null, subscriberId, ignoreCase: false));
            searchExpressions.Add(subscriberExpression);
        }

        if (searchExpressions.Count == 0)
        {
            return MemberMatchResult.NoMatch("Could not build search criteria from provided identifiers.");
        }

        // Combine expressions with OR (any identifier match)
        var combinedExpression = searchExpressions.Count == 1
            ? searchExpressions[0]
            : Expression.Or(searchExpressions.ToArray());

        // Execute search
        var searchOptions = new SearchOptions
        {
            ResourceType = "Patient",
            Expression = combinedExpression,
            MaxItemCount = 10, // Limit to detect multiple matches
            Total = TotalType.Accurate
        };

        var searchService = await _searchServiceFactory.GetSearchServiceAsync(context.TenantId, cancellationToken);
        var results = new List<SearchEntryResult>();

        await foreach (var entry in searchService.SearchStreamAsync(searchOptions, cancellationToken))
        {
            results.Add(entry);
            if (results.Count > 1)
            {
                // Stop early if multiple matches found
                break;
            }
        }

        _logger.LogDebug("Member match search returned {Count} result(s)", results.Count);

        if (results.Count == 0)
        {
            return MemberMatchResult.NoMatch();
        }

        if (results.Count > 1)
        {
            return MemberMatchResult.MultipleMatches();
        }

        // Single match found - build response
        var matchedResource = results[0];
        var memberIdentifier = BuildMemberIdentifier(matchedResource);
        var patientReference = $"Patient/{matchedResource.ResourceId}";

        _logger.LogInformation(
            "Member match successful: Patient/{PatientId}",
            matchedResource.ResourceId);

        return MemberMatchResult.Matched(memberIdentifier, patientReference);
    }

    /// <summary>
    /// Extracts identifiers from a Patient resource.
    /// </summary>
    private static List<IdentifierInfo> ExtractIdentifiers(ResourceJsonNode patient)
    {
        var identifiers = new List<IdentifierInfo>();

        var identifierArray = patient.MutableNode["identifier"];
        if (identifierArray is JsonArray array)
        {
            foreach (var item in array)
            {
                if (item is JsonObject identifierObj)
                {
                    var system = identifierObj["system"]?.GetValue<string>();
                    var value = identifierObj["value"]?.GetValue<string>();

                    if (!string.IsNullOrEmpty(value))
                    {
                        identifiers.Add(new IdentifierInfo(system, value));
                    }
                }
            }
        }

        return identifiers;
    }

    /// <summary>
    /// Extracts subscriberId from a Coverage resource.
    /// </summary>
    private static string? ExtractSubscriberId(ResourceJsonNode coverage)
    {
        return coverage.MutableNode["subscriberId"]?.GetValue<string>();
    }

    /// <summary>
    /// Builds a search expression for an identifier.
    /// </summary>
    private static Expression? BuildIdentifierExpression(IdentifierInfo identifier)
    {
        return new SearchParameterExpression(
            new SearchParameterInfo("identifier", "identifier", SearchParamType.Token),
            new StringExpression(StringOperator.Equals, FieldName.TokenCode, null, identifier.Value, ignoreCase: false));
    }

    /// <summary>
    /// Builds the MemberIdentifier response from a matched patient.
    /// </summary>
    private static JsonNode BuildMemberIdentifier(SearchEntryResult matchedResource)
    {
        // Try to extract the first identifier from the matched patient
        var resourceBytes = matchedResource.ResourceBytes;
        var resourceJson = Encoding.UTF8.GetString(resourceBytes.Span);

        // Parse the resource to get identifier
        var resourceNode = JsonNode.Parse(resourceJson);
        var identifierArray = resourceNode?["identifier"];

        if (identifierArray is JsonArray array && array.Count > 0)
        {
            // Return the first identifier (preferably one with a system)
            foreach (var item in array)
            {
                if (item is JsonObject identifierObj)
                {
                    var system = identifierObj["system"]?.GetValue<string>();
                    var value = identifierObj["value"]?.GetValue<string>();

                    if (!string.IsNullOrEmpty(value))
                    {
                        // Return a copy of the identifier
                        return new JsonObject
                        {
                            ["system"] = system != null ? JsonValue.Create(system) : null,
                            ["value"] = JsonValue.Create(value)
                        };
                    }
                }
            }
        }

        // Fallback: create identifier from resource ID
        return new JsonObject
        {
            ["value"] = JsonValue.Create(matchedResource.ResourceId)
        };
    }

    /// <summary>
    /// Simple record to hold identifier information.
    /// </summary>
    private sealed record IdentifierInfo(string? System, string Value);
}
