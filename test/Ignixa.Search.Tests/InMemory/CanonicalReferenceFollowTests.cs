// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.InMemory;
using Ignixa.Search.Models;
using Ignixa.Search.Tests.Indexing;

namespace Ignixa.Search.Tests.InMemory;

/// <summary>
/// Chains, <c>_has</c> and includes over reference parameters that select canonicals, decided from the definitions alone.
/// A parameter that selects only canonicals is refused when the query is compiled, with no data needed; one that also
/// selects references (R5 <c>RequestOrchestration-participant</c>) follows its references and skips its canonicals, as
/// does <c>_include=*</c>, whatever order the index entries are in. Regressions (fail at 78d8b45, where a canonical entry
/// threw once it was reached, and a refusal needed data to reach): every row except the guards. Guards (pass at 78d8b45,
/// mutation-checked): the canonical-last <c>Smith</c> chain and revinclude, and the fixture check.
/// </summary>
public class CanonicalReferenceFollowTests
{
    private static readonly VersionedCorpus R5 = VersionedCorpus.For(FhirVersion.R5);

    private static readonly ISearchIndexer Indexer = new CanonicalReferenceSearchIndexer(R5.Indexer, R5.Definitions);

    private const string Smith = """{"resourceType":"Patient","id":"p1","name":[{"family":"Smith"}]}""";

    private const string Ro1 = """{"resourceType":"RequestOrchestration","id":"ro1","status":"active","intent":"plan","action":[{"participant":[{"actorReference":{"reference":"Patient/p1"}},{"actorCanonical":"http://example.org/fhir/PlanDefinition/pd"}]}]}""";

    private const string Ro2 = """{"resourceType":"RequestOrchestration","id":"ro2","status":"active","intent":"plan","action":[{"participant":[{"actorReference":{"reference":"Patient/p1"}}]}]}""";

    private const string UsCorePatient = """{"resourceType":"Patient","id":"p1","meta":{"profile":["http://hl7.org/fhir/us/core/StructureDefinition/us-core-patient|7.0.0"]},"name":[{"family":"Smith"}]}""";

    private const string UsCoreObservation = """{"resourceType":"Observation","id":"o1","meta":{"profile":["http://hl7.org/fhir/us/core/StructureDefinition/us-core-observation-lab"]},"status":"final","code":{"text":"x"},"subject":{"reference":"Patient/p1"}}""";

    public static TheoryData<bool> CanonicalFirst => new(true, false);

    [Fact]
    public void GivenTheFixtures_WhenIndexed_ThenTheirCanonicalsAreIndexedBesideTheirReferences()
    {
        Values(Ro1, "participant").ShouldBe(["ReferenceSearchValue", "UriSearchValue"], ignoreOrder: true);
        Values(Ro2, "participant").ShouldBe(["ReferenceSearchValue"]);
        Values(UsCoreObservation, "_profile").ShouldBe(["UriSearchValue"]);
        Values(UsCoreObservation, "subject").ShouldBe(["ReferenceSearchValue"]);
    }

    [Theory]
    [MemberData(nameof(CanonicalFirst))]
    public void GivenAMixedParameter_WhenChainedToAMatchingTarget_ThenItsReferencesAreFollowed(bool canonicalFirst)
    {
        Search(canonicalFirst, "RequestOrchestration", "participant:Patient.name=Smith").ShouldBe(["ro1", "ro2"]);
    }

    [Theory]
    [MemberData(nameof(CanonicalFirst))]
    public void GivenAMixedParameter_WhenChainedToNoMatchingTarget_ThenNothingMatchesAndNothingIsRefused(bool canonicalFirst)
    {
        Search(canonicalFirst, "RequestOrchestration", "participant:Patient.name=Jones").ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(CanonicalFirst))]
    public void GivenAMixedParameter_WhenReverseChained_ThenItsReferencesAreFollowed(bool canonicalFirst)
    {
        Search(canonicalFirst, "Patient", "_has:RequestOrchestration:participant:_id=ro1").ShouldBe(["p1"]);
    }

    [Theory]
    [MemberData(nameof(CanonicalFirst))]
    public void GivenAMixedParameter_WhenIncluded_ThenItsReferencesAreFollowedAndItsCanonicalsSkipped(bool canonicalFirst)
    {
        Include(Source(canonicalFirst, Smith, Ro1, Ro2), "RequestOrchestration", "_id=ro1&_include=RequestOrchestration:participant").ShouldBe(["Patient/p1"]);
    }

    [Theory]
    [MemberData(nameof(CanonicalFirst))]
    public void GivenAMixedParameter_WhenRevIncluded_ThenEveryReferencingResourceIsIncluded(bool canonicalFirst)
    {
        Include(Source(canonicalFirst, Smith, Ro1, Ro2), "Patient", "_revinclude=RequestOrchestration:participant").ShouldBe(["RequestOrchestration/ro1", "RequestOrchestration/ro2"]);
    }

    [Theory]
    [InlineData("_include=*", true)]
    [InlineData("_include=*", false)]
    [InlineData("_include=Observation:*", true)]
    [InlineData("_include=Observation:*", false)]
    public void GivenProfiledResources_WhenIncludedByWildcard_ThenOnlyTheirReferencesAreFollowed(string query, bool canonicalFirst)
    {
        Include(Source(canonicalFirst, UsCorePatient, UsCoreObservation), "Observation", query).ShouldBe(["Patient/p1"]);
    }

    [Theory]
    [InlineData("Patient", "_profile:StructureDefinition.name=x")]
    [InlineData("StructureDefinition", "_has:Patient:_profile:_id=typed")]
    [InlineData("QuestionnaireResponse", "questionnaire:Questionnaire.name=x")]
    [InlineData("Questionnaire", "_has:QuestionnaireResponse:questionnaire:_id=qr")]
    [InlineData("CarePlan", "instantiates-canonical:PlanDefinition.name=x")]
    [InlineData("Patient", "_include=Patient:_profile")]
    [InlineData("StructureDefinition", "_revinclude=Patient:_profile")]
    [InlineData("QuestionnaireResponse", "_include=QuestionnaireResponse:questionnaire")]
    [InlineData("PlanDefinition", "_revinclude=CarePlan:instantiates-canonical")]
    public void GivenACanonicalOnlyParameterAndNoData_WhenTheSearchIsCompiled_ThenItIsRefused(string resourceType, string query)
    {
        var engine = new InMemorySearchEngine(new ListSearchSource([]), InMemorySearchFeatures.All);
        SearchOptions options = R5.Options(resourceType, query);
        options.UnsupportedParams.ShouldBeEmpty();

        Should.Throw<SearchOperationNotSupportedException>(() => engine.Search(options))
            .Issues.ShouldHaveSingleItem().Diagnostics.ShouldNotBeNull().ShouldContain("canonical resolution is not supported");
    }

    [Theory]
    [InlineData("Patient", "_include=Patient:_profile")]
    [InlineData("StructureDefinition", "_revinclude=Patient:_profile")]
    public void GivenACanonicalOnlyIncludeAndAnEmptyPage_WhenIncludesAreResolved_ThenItIsRefused(string resourceType, string query)
    {
        var engine = new InMemorySearchEngine(new ListSearchSource([]), InMemorySearchFeatures.All);
        SearchOptions options = R5.Options(resourceType, query);

        Should.Throw<SearchOperationNotSupportedException>(() => engine.Include([], options, 50))
            .Issues.ShouldHaveSingleItem().Diagnostics.ShouldNotBeNull().ShouldContain("canonical resolution is not supported");
    }

    private static string[] Values(string json, string code) =>
        [.. R5.Index(Indexer, json).Index.Where(entry => entry.SearchParameter.Code == code).Select(entry => entry.Value.GetType().Name)];

    private static string[] Search(bool canonicalFirst, string resourceType, string query)
    {
        var engine = new InMemorySearchEngine(Source(canonicalFirst, Smith, Ro1, Ro2), InMemorySearchFeatures.All);
        SearchOptions options = R5.Options(resourceType, query);
        options.UnsupportedParams.ShouldBeEmpty();

        return [.. engine.Search(options).Select(match => match.Location.Id)];
    }

    private static string[] Include(ListSearchSource source, string resourceType, string query)
    {
        var engine = new InMemorySearchEngine(source, InMemorySearchFeatures.All);
        SearchOptions options = R5.Options(resourceType, query);
        options.UnsupportedParams.ShouldBeEmpty();

        return [.. engine.Include(engine.Search(options), options, 50).Select(included => $"{included.Location.ResourceType}/{included.Location.Id}")];
    }

    /// <summary>
    /// The resources indexed with their canonical entries placed first or last; the decorator appends them last, so the
    /// first order is the one a lazily evaluated refusal reached before any reference.
    /// </summary>
    private static ListSearchSource Source(bool canonicalFirst, params string[] resources) =>
        new([.. resources.Select(json => R5.Index(Indexer, json)).Select(resource => (resource.Location, Ordered(resource.Index, canonicalFirst)))]);

    private static IReadOnlyCollection<SearchIndexEntry> Ordered(IReadOnlyCollection<SearchIndexEntry> index, bool canonicalFirst) =>
        [.. index.OrderBy(entry => entry.Value is UriSearchValue == canonicalFirst ? 0 : 1)];
}
