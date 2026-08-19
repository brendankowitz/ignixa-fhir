// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.DataLayer.SqlEntityFramework.Entities;
using Ignixa.DataLayer.SqlEntityFramework.Indexing;
using Ignixa.DataLayer.SqlEntityFramework.Search;
using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Ignixa.DataLayer.SqlEntityFramework.IntegrationTests;

/// <summary>
/// Pins that a reference search is satisfied by a single index row, not by the union of rows.
/// <para>
/// <c>performer=Organization/1</c> lowers to <c>And(StringEquals(ReferenceResourceType, "Organization"),
/// StringEquals(ReferenceResourceId, "1"))</c> — a statement about one stored reference. Evaluating that
/// conjunction as a set intersection of per-field resource-id sets instead lets a resource carrying several
/// rows on the same parameter satisfy the type from one row and the id from a different row, so a resource
/// that references neither <c>Organization/1</c> nor anything like it matches. Every repeating reference
/// parameter indexes exactly that shape: <c>Observation.performer</c>, <c>Composition.author</c>,
/// <c>CareTeam.participant.member</c>, and so on.
/// </para>
/// <para>
/// This is the reference twin of <see cref="DateTimeConjunctionRowScopeTests"/>, and it is the more common
/// failure of the two: reference search is ubiquitous, and a resource needs only two references of differing
/// target type to become a false positive.
/// </para>
/// </summary>
public sealed class ReferenceConjunctionRowScopeTests : IDisposable
{
    private const short ObservationResourceTypeId = 3;
    private const short PractitionerResourceTypeId = 4;
    private const short OrganizationResourceTypeId = 5;
    private const short PerformerSearchParamId = 9;
    private const string PerformerParameterUri = "http://hl7.org/fhir/SearchParameter/Observation-performer";

    /// <summary>Resource referencing Practitioner/1 and Organization/2 — no row is Organization/1.</summary>
    private const long MismatchedPair = 1;

    /// <summary>Resource whose only row is Organization/1.</summary>
    private const long SingleExactRow = 2;

    /// <summary>Resource with two rows, one of them Organization/1.</summary>
    private const long PairWithOneExactRow = 3;

    /// <summary>Resource whose only row shares the id but not the type.</summary>
    private const long IdOnlyRow = 4;

    /// <summary>Resource referencing Organization/1 on another server — same type and id, different base.</summary>
    private const long ExternalRow = 5;

    private const string OtherServerBaseUri = "http://other.example.org/fhir/";

    private readonly FhirDbContext _context;
    private readonly SearchIndexReferenceDataCache _cache;
    private readonly SearchParameterQueryGenerator _generator;

    public ReferenceConjunctionRowScopeTests()
    {
        var options = new DbContextOptionsBuilder<FhirDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new FhirDbContext(options);
        _cache = new SearchIndexReferenceDataCache(_context, NullLogger<SearchIndexReferenceDataCache>.Instance);

        _context.ResourceTypes.Add(new ResourceTypeEntity { ResourceTypeId = ObservationResourceTypeId, Name = "Observation" });
        _context.ResourceTypes.Add(new ResourceTypeEntity { ResourceTypeId = PractitionerResourceTypeId, Name = "Practitioner" });
        _context.ResourceTypes.Add(new ResourceTypeEntity { ResourceTypeId = OrganizationResourceTypeId, Name = "Organization" });
        _context.SearchParams.Add(new SearchParamEntity { SearchParamId = PerformerSearchParamId, Uri = PerformerParameterUri, Status = "Enabled" });
        _context.SaveChanges();

        _generator = new SearchParameterQueryGenerator(
            _context,
            _cache,
            NullLogger<SearchParameterQueryGenerator>.Instance,
            new CompositeSearchParameterQueryGenerator(
                _context,
                _cache,
                NullLogger<CompositeSearchParameterQueryGenerator>.Instance));

        SeedRows();
    }

    [Fact]
    public async Task GivenAResourceWhoseRowsSplitTheTypeAndTheId_WhenSearchingForAReference_ThenItDoesNotMatch()
    {
        // Arrange — resource 1 references Practitioner/1 and Organization/2. performer=Organization/1 asks
        // for one row that is both; the Organization/2 row supplies the type and the Practitioner/1 row
        // supplies the id, but no row supplies both. Resources 2 and 3 each own a row that does.

        // Act
        var matches = await MatchAsync(ReferenceExpression("Organization", "1"));

        // Assert — resource 5 is in the answer because a relative search leaves the base unconstrained,
        // which is the ReferenceKind.InternalOrExternal contract. The claim under test is that 1 is absent.
        matches.ShouldBe([SingleExactRow, PairWithOneExactRow, ExternalRow], "performer=Organization/1");
    }

    [Fact]
    public async Task GivenAReferenceMatchingNoStoredRow_WhenSearchingForIt_ThenNothingMatches()
    {
        // Arrange — nothing anywhere references Practitioner/2. Resource 1 alone can manufacture it by
        // pairing the type of its Practitioner/1 row with the id of its Organization/2 row, so the correct
        // answer is empty and any match at all is assembled across rows.

        // Act
        var matches = await MatchAsync(ReferenceExpression("Practitioner", "2"));

        // Assert
        matches.ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenAReferenceWithNoResourceType_WhenSearchingForIt_ThenEveryRowCarryingThatIdMatches()
    {
        // Arrange — a type-less reference lowers to a single StringEquals, not a conjunction, so it never
        // reaches the fused path. Pinned so row scoping cannot be mistaken for an added type constraint:
        // performer=1 still matches a row of any target type carrying that id.

        // Act
        var matches = await MatchAsync(ReferenceExpression(resourceType: null, "1"));

        // Assert
        matches.ShouldBe([MismatchedPair, SingleExactRow, PairWithOneExactRow, IdOnlyRow, ExternalRow], "performer=1");
    }

    [Fact]
    public async Task GivenAReferenceToThisServer_WhenSearchingForIt_ThenOnlyRowsWithNoBaseMatch()
    {
        // Arrange — an absolute reference naming this server's own base parses as ReferenceKind.Internal and
        // lowers to And(Missing(ReferenceBaseUri), type, id). "This server" is the null base column, so the
        // otherwise identical row on another server must be excluded. Before the fused path this shape threw
        // NotSupportedException: ProcessExpressionAsync has no arm for MissingFieldExpression.

        // Act
        var matches = await MatchAsync(InternalReferenceExpression("Organization", "1"));

        // Assert
        matches.ShouldBe([SingleExactRow, PairWithOneExactRow], "performer=<this server>/Organization/1");
    }

    [Fact]
    public async Task GivenAReferenceToAnotherServer_WhenSearchingForIt_ThenOnlyRowsWithThatBaseMatch()
    {
        // Arrange — the mirror of the above. An absolute reference to a foreign base lowers to
        // And(ReferenceBaseUri, type, id) and must exclude the local rows carrying the same type and id.
        // This shape also threw before: ProcessStringExpressionAsync has no arm for ReferenceBaseUri.

        // Act
        var matches = await MatchAsync(ExternalReferenceExpression(OtherServerBaseUri, "Organization", "1"));

        // Assert
        matches.ShouldBe([ExternalRow], $"performer={OtherServerBaseUri}Organization/1");
    }

    [Fact]
    public async Task GivenAnInternalReferenceMatchingNoStoredRow_WhenSearchingForIt_ThenNothingMatches()
    {
        // Arrange — row scoping has to hold for the three-conjunct shape too, not just the two-conjunct one.
        // Resource 1's rows are both local, so its Practitioner/1 row satisfies the missing base and the type
        // while its Organization/2 row satisfies the id.

        // Act
        var matches = await MatchAsync(InternalReferenceExpression("Practitioner", "2"));

        // Assert
        matches.ShouldBeEmpty();
    }

    public void Dispose()
    {
        _cache.Dispose();
        _context.Dispose();
    }

    private void SeedRows()
    {
        AddRow(MismatchedPair, PractitionerResourceTypeId, "1");
        AddRow(MismatchedPair, OrganizationResourceTypeId, "2");
        AddRow(SingleExactRow, OrganizationResourceTypeId, "1");
        AddRow(PairWithOneExactRow, PractitionerResourceTypeId, "9");
        AddRow(PairWithOneExactRow, OrganizationResourceTypeId, "1");
        AddRow(IdOnlyRow, PractitionerResourceTypeId, "1");
        AddRow(ExternalRow, OrganizationResourceTypeId, "1", OtherServerBaseUri);

        _context.SaveChanges();
    }

    private void AddRow(long surrogateId, short referenceResourceTypeId, string referenceResourceId, string? baseUri = null)
    {
        _context.ReferenceSearchParams.Add(new ReferenceSearchParamEntity
        {
            ResourceTypeId = ObservationResourceTypeId,
            ResourceSurrogateId = surrogateId,
            SearchParamId = PerformerSearchParamId,
            BaseUri = baseUri,
            ReferenceResourceTypeId = referenceResourceTypeId,
            ReferenceResourceId = referenceResourceId
        });
    }

    private async Task<IReadOnlyList<long>> MatchAsync(SearchParameterExpression expression)
    {
        var query = await _generator.GenerateQueryAsync(ObservationResourceTypeId, expression, CancellationToken.None);
        return [.. query.ToList().Distinct().Order()];
    }

    private static SearchParameterExpression ReferenceExpression(string? resourceType, string resourceId) =>
        Lower(new ReferenceSearchValue(ReferenceKind.InternalOrExternal, baseUri: null!, resourceType!, resourceId));

    private static SearchParameterExpression InternalReferenceExpression(string resourceType, string resourceId) =>
        Lower(new ReferenceSearchValue(ReferenceKind.Internal, baseUri: null!, resourceType, resourceId));

    private static SearchParameterExpression ExternalReferenceExpression(string baseUri, string resourceType, string resourceId) =>
        Lower(new ReferenceSearchValue(ReferenceKind.External, new Uri(baseUri), resourceType, resourceId));

    private static SearchParameterExpression Lower(ReferenceSearchValue value)
    {
        var parameter = PerformerParameter();

        return new SearchParameterExpression(
            parameter,
            LegacyExpressionLowerer.LowerToLegacy(
                new SearchParameterPredicateExpression(parameter, SearchComparator.Eq, modifier: null, value)));
    }

    private static SearchParameterInfo PerformerParameter() =>
        new("performer", "performer", SearchParamType.Reference, new Uri(PerformerParameterUri));
}
