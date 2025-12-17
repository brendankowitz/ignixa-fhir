// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.Application.Features.Search;
using Ignixa.Application.Infrastructure;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.FhirPath.Evaluation;
using Ignixa.Search.Expressions;
using Ignixa.Search.Models;
using Ignixa.Serialization;
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
    /// <summary>
    /// The search parameter name for identifier-based searches.
    /// </summary>
    private const string IdentifierSearchParamName = "identifier";

    private readonly ISearchServiceFactory _searchServiceFactory;
    private readonly IFhirRequestContextAccessor _contextAccessor;
    private readonly IFhirVersionContext _versionContext;
    private readonly ILogger<DefaultMemberMatchStrategy> _logger;

    public DefaultMemberMatchStrategy(
        ISearchServiceFactory searchServiceFactory,
        IFhirRequestContextAccessor contextAccessor,
        IFhirVersionContext versionContext,
        ILogger<DefaultMemberMatchStrategy> logger)
    {
        _searchServiceFactory = searchServiceFactory ?? throw new ArgumentNullException(nameof(searchServiceFactory));
        _contextAccessor = contextAccessor ?? throw new ArgumentNullException(nameof(contextAccessor));
        _versionContext = versionContext ?? throw new ArgumentNullException(nameof(versionContext));
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

        // Get schema provider for FhirPath evaluation
        var schemaProvider = _versionContext.GetBaseSchemaProvider(context.FhirVersion);

        // Convert to IElement for FhirPath extraction
        var patientElement = memberPatient.ToElement(schemaProvider);
        var coverageElement = coverageToMatch.ToElement(schemaProvider);

        // Extract identifiers from input resources using FhirPath
        var patientIdentifiers = ExtractIdentifiers(patientElement);
        var subscriberId = ExtractSubscriberId(coverageElement);

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
            var subscriberExpression = BuildIdentifierExpression(new IdentifierInfo(null, subscriberId));
            if (subscriberExpression != null)
            {
                searchExpressions.Add(subscriberExpression);
            }
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
            ResourceType = KnownResourceTypes.Patient,
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
        var matchedNode = JsonSourceNodeFactory.Parse(matchedResource.ResourceBytes);
        var matchedElement = matchedNode.ToElement(schemaProvider);
        var memberIdentifier = BuildMemberIdentifier(matchedElement);
        var patientReference = $"{KnownResourceTypes.Patient}/{matchedResource.ResourceId}";

        _logger.LogInformation(
            "Member match successful: Patient/{PatientId}",
            matchedResource.ResourceId);

        return MemberMatchResult.Matched(memberIdentifier, patientReference);
    }

    /// <summary>
    /// Extracts identifiers from a Patient resource using FhirPath.
    /// </summary>
    private static List<IdentifierInfo> ExtractIdentifiers(IElement patient)
    {
        var identifiers = new List<IdentifierInfo>();

        foreach (var identifier in patient.Select("identifier"))
        {
            var system = identifier.Scalar("system") as string;
            var value = identifier.Scalar("value") as string;

            if (!string.IsNullOrEmpty(value))
            {
                identifiers.Add(new IdentifierInfo(system, value));
            }
        }

        return identifiers;
    }

    /// <summary>
    /// Extracts subscriberId from a Coverage resource using FhirPath.
    /// </summary>
    private static string? ExtractSubscriberId(IElement coverage)
    {
        return coverage.Scalar("subscriberId") as string;
    }

    /// <summary>
    /// Builds a search expression for an identifier.
    /// </summary>
    private static Expression? BuildIdentifierExpression(IdentifierInfo identifier)
    {
        return new SearchParameterExpression(
            new SearchParameterInfo(IdentifierSearchParamName, IdentifierSearchParamName, SearchParamType.Token),
            new StringExpression(StringOperator.Equals, FieldName.TokenCode, null, identifier.Value, ignoreCase: false));
    }

    /// <summary>
    /// Builds the MemberIdentifier response from a matched patient using FhirPath.
    /// </summary>
    private static JsonNode BuildMemberIdentifier(IElement matchedPatient)
    {
        // Extract the first identifier with a value using FhirPath
        foreach (var identifier in matchedPatient.Select("identifier"))
        {
            var system = identifier.Scalar("system") as string;
            var value = identifier.Scalar("value") as string;

            if (!string.IsNullOrEmpty(value))
            {
                return new JsonObject
                {
                    ["system"] = system != null ? JsonValue.Create(system) : null,
                    ["value"] = JsonValue.Create(value)
                };
            }
        }

        // Fallback: create identifier from resource ID (should always exist for stored resources)
        var resourceId = matchedPatient.Scalar("id") as string;
        if (string.IsNullOrEmpty(resourceId))
        {
            throw new InvalidOperationException("Matched patient resource has no id or identifier - this indicates a data integrity issue.");
        }

        return new JsonObject
        {
            ["value"] = JsonValue.Create(resourceId)
        };
    }

    /// <summary>
    /// Simple record to hold identifier information.
    /// </summary>
    private sealed record IdentifierInfo(string? System, string Value);
}
