// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.Application.Features.Search;
using Ignixa.Application.Infrastructure;
using Ignixa.Application.Operations.Features.MemberMatch;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Search.Expressions;
using Ignixa.Search.Models;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Generated;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Ignixa.Application.Tests.Features.MemberMatch;

/// <summary>
/// Covers <see cref="DefaultMemberMatchStrategy"/>'s reading of <c>Coverage.subscriberId</c>, which is
/// declared differently across versions: <c>string 0..1</c> in STU3/R4/R4B, <c>Identifier 0..*</c> in
/// R5 and R6.
/// </summary>
/// <remarks>
/// These assert on the search criteria the strategy actually sends, not on the match outcome, because
/// the defect being pinned was invisible in the outcome: reading the element with <c>Scalar</c>
/// returned null for every R5 Coverage - complex elements have no primitive value, and a repeating
/// element has no single one - so $member-match dropped a caller-supplied criterion and answered as
/// though it had never been given. A matching operation that quietly weakens its own criteria is worse
/// than one that fails loudly, so the contract under test is "every subscriberId the caller supplied
/// reaches the search".
/// </remarks>
public class DefaultMemberMatchStrategySubscriberIdTests
{
    private readonly ISearchService _searchService = Substitute.For<ISearchService>();
    private readonly ISearchServiceFactory _searchServiceFactory = Substitute.For<ISearchServiceFactory>();
    private readonly IFhirRequestContextAccessor _contextAccessor = Substitute.For<IFhirRequestContextAccessor>();
    private readonly IFhirVersionContext _versionContext = Substitute.For<IFhirVersionContext>();

    private const string PatientWithoutIdentifiers = """
    {
        "resourceType": "Patient",
        "id": "patient-123"
    }
    """;

    private static readonly string[] BothSubscriberIds = ["SUB-A", "SUB-B"];
    private static readonly string[] OnlySubscriberId = ["SUB-ONLY"];
    private static readonly string[] R4SubscriberId = ["SUB12345"];
    private static readonly string[] MemberAndBothSubscriberIds = ["MEM-1", "SUB-A", "SUB-B"];

    [Fact]
    public async Task GivenAnR5CoverageWithTwoSubscriberIds_WhenMatching_ThenBothReachTheSearch()
    {
        // Arrange
        var coverage = """
        {
            "resourceType": "Coverage",
            "id": "coverage-123",
            "subscriberId": [
                { "system": "http://example.org/subscribers", "value": "SUB-A" },
                { "system": "http://example.org/legacy-subscribers", "value": "SUB-B" }
            ]
        }
        """;

        var strategy = CreateStrategy(FhirVersion.R5);

        // Act
        var result = await strategy.MatchAsync(
            Parse(PatientWithoutIdentifiers), Parse(coverage), null, CancellationToken.None);

        // Assert
        var searchedValues = CapturedTokenValues();
        searchedValues.ShouldBe(BothSubscriberIds, ignoreOrder: true);
        result.ShouldNotBeNull();
    }

    [Fact]
    public async Task GivenAnR5CoverageWithOneSubscriberId_WhenMatching_ThenItReachesTheSearch()
    {
        // Arrange - a single R5 subscriberId is still an Identifier, so Scalar returned null here too
        var coverage = """
        {
            "resourceType": "Coverage",
            "id": "coverage-123",
            "subscriberId": [
                { "system": "http://example.org/subscribers", "value": "SUB-ONLY" }
            ]
        }
        """;

        var strategy = CreateStrategy(FhirVersion.R5);

        // Act
        await strategy.MatchAsync(
            Parse(PatientWithoutIdentifiers), Parse(coverage), null, CancellationToken.None);

        // Assert
        CapturedTokenValues().ShouldBe(OnlySubscriberId);
    }

    [Fact]
    public async Task GivenAnR4CoverageWithAStringSubscriberId_WhenMatching_ThenItReachesTheSearch()
    {
        // Arrange - the pre-R5 shape must keep working; this is the regression guard on the fix
        var coverage = """
        {
            "resourceType": "Coverage",
            "id": "coverage-123",
            "subscriberId": "SUB12345"
        }
        """;

        var strategy = CreateStrategy(FhirVersion.R4);

        // Act
        await strategy.MatchAsync(
            Parse(PatientWithoutIdentifiers), Parse(coverage), null, CancellationToken.None);

        // Assert
        CapturedTokenValues().ShouldBe(R4SubscriberId);
    }

    [Fact]
    public async Task GivenAnR5CoverageWithSubscriberIdsAndPatientIdentifiers_WhenMatching_ThenAllAreSearched()
    {
        // Arrange
        var patient = """
        {
            "resourceType": "Patient",
            "id": "patient-123",
            "identifier": [ { "system": "http://example.org/members", "value": "MEM-1" } ]
        }
        """;

        var coverage = """
        {
            "resourceType": "Coverage",
            "id": "coverage-123",
            "subscriberId": [
                { "value": "SUB-A" },
                { "value": "SUB-B" }
            ]
        }
        """;

        var strategy = CreateStrategy(FhirVersion.R5);

        // Act
        await strategy.MatchAsync(Parse(patient), Parse(coverage), null, CancellationToken.None);

        // Assert
        CapturedTokenValues().ShouldBe(MemberAndBothSubscriberIds, ignoreOrder: true);
    }

    [Fact]
    public async Task GivenAnR5CoverageWhoseOnlyCriterionIsASubscriberId_WhenMatching_ThenItIsNotReportedAsNoIdentifiers()
    {
        // Arrange - Scalar returning null used to collapse this into "No identifiers provided",
        // a 4xx blaming the caller for input they had in fact supplied
        var coverage = """
        {
            "resourceType": "Coverage",
            "id": "coverage-123",
            "subscriberId": [ { "value": "SUB-ONLY" } ]
        }
        """;

        var strategy = CreateStrategy(FhirVersion.R5);

        // Act
        var result = await strategy.MatchAsync(
            Parse(PatientWithoutIdentifiers), Parse(coverage), null, CancellationToken.None);

        // Assert
        result.ErrorMessage.ShouldNotContain("No identifiers provided");
        CapturedTokenValues().ShouldBe(OnlySubscriberId);
    }

    [Fact]
    public async Task GivenACoverageWithNoSubscriberIdAndAPatientWithNoIdentifiers_WhenMatching_ThenReportsNoIdentifiers()
    {
        // Arrange
        var coverage = """
        {
            "resourceType": "Coverage",
            "id": "coverage-123"
        }
        """;

        var strategy = CreateStrategy(FhirVersion.R5);

        // Act
        var result = await strategy.MatchAsync(
            Parse(PatientWithoutIdentifiers), Parse(coverage), null, CancellationToken.None);

        // Assert
        result.Success.ShouldBeFalse();
        result.ErrorMessage.ShouldContain("No identifiers provided");
    }

    private DefaultMemberMatchStrategy CreateStrategy(FhirVersion version)
    {
        var requestContext = Substitute.For<IFhirRequestContext>();
        requestContext.FhirVersion.Returns(version);
        requestContext.TenantId.Returns(1);
        _contextAccessor.RequestContext.Returns(requestContext);

        _versionContext.GetBaseSchemaProvider(version).Returns(CreateSchemaProvider(version));

        _searchServiceFactory.GetSearchServiceAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_searchService);

        _searchService.SearchStreamAsync(Arg.Any<SearchOptions>(), Arg.Any<CancellationToken>())
            .Returns(_ => NoResults());

        return new DefaultMemberMatchStrategy(
            _searchServiceFactory,
            _contextAccessor,
            _versionContext,
            NullLogger<DefaultMemberMatchStrategy>.Instance);
    }

    private static IFhirSchemaProvider CreateSchemaProvider(FhirVersion version) => version switch
    {
        FhirVersion.Stu3 => new STU3CoreSchemaProvider(),
        FhirVersion.R4 => new R4CoreSchemaProvider(),
        FhirVersion.R4B => new R4BCoreSchemaProvider(),
        FhirVersion.R5 => new R5CoreSchemaProvider(),
        _ => throw new ArgumentOutOfRangeException(nameof(version), version, "Unsupported version in this fixture.")
    };

    /// <summary>
    /// Reads back the identifier values the strategy actually put into the search expression, which is
    /// the observable the caller's criteria have to survive into.
    /// </summary>
    private IReadOnlyList<string> CapturedTokenValues()
    {
        var call = _searchService.ReceivedCalls()
            .Single(c => c.GetMethodInfo().Name == nameof(ISearchService.SearchStreamAsync));

        var searchOptions = (SearchOptions)call.GetArguments()[0]!;

        var values = new List<string>();
        CollectTokenValues(searchOptions.Expression, values);
        return values;
    }

    private static void CollectTokenValues(Expression? expression, List<string> values)
    {
        switch (expression)
        {
            case MultiaryExpression multiary:
                foreach (var child in multiary.Expressions)
                {
                    CollectTokenValues(child, values);
                }

                break;
            case SearchParameterExpression searchParameter:
                CollectTokenValues(searchParameter.Expression, values);
                break;
            case StringExpression stringExpression:
                values.Add(stringExpression.Value);
                break;
        }
    }

    private static ResourceJsonNode Parse(string json) => ResourceJsonNode.Parse(json);

#pragma warning disable CS1998
    private static async IAsyncEnumerable<SearchEntryResult> NoResults()
    {
        yield break;
    }
#pragma warning restore CS1998
}
