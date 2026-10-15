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
/// Regressions for reference parameters over a canonical in memory, through <see cref="CanonicalReferenceSearchIndexer"/>.
/// At 206143a they indexed nothing; at b096951 they indexed as references through the unanchored <c>Type/id</c> parser,
/// so profiles differing after an underscore collided and a versioned search missed an untyped canonical. Guards (pass
/// at b096951, mutation-checked): the R4 <c>instantiates-canonical</c> match and, in the version matrix, the unversioned
/// queries, <c>q|2.0</c> and <c>:missing=false</c>.
/// </summary>
public class CanonicalReferenceSearchTests
{
    private const string KbvPatient = "https://fhir.kbv.de/StructureDefinition/KBV_PR_Base_Patient";

    private const string KbvObservation = "https://fhir.kbv.de/StructureDefinition/KBV_PR_Base_Observation";

    private static readonly VersionedCorpus R5 = VersionedCorpus.For(FhirVersion.R5);

    private static readonly ISearchIndexer Indexer = new CanonicalReferenceSearchIndexer(R5.Indexer, R5.Definitions, R5.Schema);

    private static readonly string[] Profiled =
    [
        Patient("typed", "http://example.org/fhir/StructureDefinition/p"),
        Patient("typed-v2", "http://example.org/fhir/StructureDefinition/p|2.0"),
        Patient("untyped", "http://example.org/profiles/q"),
        Patient("untyped-v1", "http://example.org/profiles/q|1.0"),
        Patient("kbv-patient", KbvPatient),
        Patient("kbv-observation", KbvObservation),
    ];

    [Theory]
    [InlineData("_profile=" + KbvPatient, "kbv-patient")]
    [InlineData("_profile=" + KbvObservation, "kbv-observation")]
    public void GivenProfilesThatDifferAfterAnUnderscore_WhenSearched_ThenEachMatchesOnlyItsOwnResource(string query, string expected)
    {
        R5.Match(Indexer, "Patient", query, Profiled).ShouldBe([expected]);
    }

    [Theory]
    [InlineData("_profile=http://example.org/fhir/StructureDefinition/p", "typed,typed-v2")]
    [InlineData("_profile=http://example.org/fhir/StructureDefinition/p|2.0", "typed-v2")]
    [InlineData("_profile=http://example.org/fhir/StructureDefinition/p|3.0", "")]
    [InlineData("_profile=http://example.org/profiles/q", "untyped,untyped-v1")]
    [InlineData("_profile=http://example.org/profiles/q|1.0", "untyped-v1")]
    [InlineData("_profile=http://example.org/profiles/q|2.0", "")]
    [InlineData("_profile=http://example.org/profiles/q,http://example.org/fhir/StructureDefinition/p|2.0", "typed-v2,untyped,untyped-v1")]
    [InlineData("_profile:missing=false", "typed,typed-v2,untyped,untyped-v1,kbv-patient,kbv-observation")]
    public void GivenVersionedAndUnversionedCanonicals_WhenSearched_ThenAUrlMatchesEveryVersionAndAUrlWithAVersionOnlyThatOne(string query, string expected)
    {
        R5.Match(Indexer, "Patient", query, Profiled).ShouldBe(expected.Length == 0 ? [] : expected.Split(','));
    }

    [Fact]
    public void GivenAProfiledResource_WhenIndexed_ThenTheCanonicalIsAUriWithItsVersionSplit()
    {
        UriSearchValue profile = R5.Index(Indexer, Patient("p", "http://example.org/profiles/q|1.0")).Index
            .Where(entry => entry.SearchParameter.Code == "_profile")
            .Select(entry => entry.Value)
            .ShouldHaveSingleItem()
            .ShouldBeOfType<UriSearchValue>();

        (profile.Uri, profile.Version).ShouldBe(("http://example.org/profiles/q", "1.0"));
    }

    [Fact]
    public void GivenAnR4ReferenceParameterOverACanonical_WhenSearched_ThenItMatchesThroughTheSameIndexer()
    {
        VersionedCorpus r4 = VersionedCorpus.For(FhirVersion.R4);
        var indexer = new CanonicalReferenceSearchIndexer(r4.Indexer, r4.Definitions, r4.Schema);

        r4.Match(
                indexer,
                "CarePlan",
                "instantiates-canonical=http://example.org/PlanDefinition/pd",
                """{"resourceType":"CarePlan","id":"c","status":"active","intent":"plan","subject":{"reference":"Patient/p"},"instantiatesCanonical":["http://example.org/PlanDefinition/pd|1"]}""",
                """{"resourceType":"CarePlan","id":"other","status":"active","intent":"plan","subject":{"reference":"Patient/p"},"instantiatesCanonical":["http://example.org/PlanDefinition/pd2"]}""")
            .ShouldBe(["c"]);
    }

    [Theory]
    [InlineData("_profile=http://example.org/profiles/q&_include=Patient:_profile")]
    [InlineData("_profile=http://example.org/profiles/q&_include=Patient:*")]
    public void GivenAnIncludeThroughACanonicalParameter_WhenResolved_ThenItIsRefusedRatherThanFollowingNothing(string query)
    {
        var source = new ListSearchSource([.. Profiled.Select(json => R5.Index(Indexer, json))]);
        var engine = new InMemorySearchEngine(source, InMemorySearchFeatures.All);
        SearchOptions options = R5.Options("Patient", query);
        options.UnsupportedParams.ShouldBeEmpty();

        SearchOperationNotSupportedException refused = Should.Throw<SearchOperationNotSupportedException>(
            () => engine.Include(engine.Search(options), options, 50).ToList());

        refused.Issues.ShouldHaveSingleItem().Diagnostics.ShouldNotBeNull().ShouldContain("canonical resolution is not supported");
    }

    [Theory]
    [InlineData("Patient", "_profile:StructureDefinition.name=x")]
    [InlineData("StructureDefinition", "_has:Patient:_profile:_id=typed")]
    public void GivenAChainThroughACanonicalParameter_WhenSearched_ThenItIsRefusedRatherThanFollowingNothing(string resourceType, string query)
    {
        var source = new ListSearchSource([.. Profiled.Select(json => R5.Index(Indexer, json)), R5.Index(Indexer, """{"resourceType":"StructureDefinition","id":"sd","url":"http://example.org/profiles/q","name":"x","status":"active","kind":"resource","abstract":false,"type":"Patient"}""")]);
        var engine = new InMemorySearchEngine(source, InMemorySearchFeatures.All);
        SearchOptions options = R5.Options(resourceType, query);
        options.UnsupportedParams.ShouldBeEmpty();

        Should.Throw<SearchOperationNotSupportedException>(() => engine.Search(options).ToList())
            .Issues.ShouldHaveSingleItem().Diagnostics.ShouldNotBeNull().ShouldContain("canonical resolution is not supported");
    }

    private static string Patient(string id, string profile) =>
        $$$"""{"resourceType":"Patient","id":"{{{id}}}","meta":{"profile":["{{{profile}}}"]}}""";
}
