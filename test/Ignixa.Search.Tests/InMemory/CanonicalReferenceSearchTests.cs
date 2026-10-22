// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.InMemory;
using Ignixa.Search.Models;
using Ignixa.Search.Tests.Indexing;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Search.Tests.InMemory;

/// <summary>
/// Regressions for reference parameters over a canonical in memory, through <see cref="CanonicalReferenceSearchIndexer"/>.
/// At 206143a they indexed nothing; at b096951 they indexed as references through the unanchored <c>Type/id</c> parser,
/// so profiles differing after an underscore collided and a versioned search missed an untyped canonical. Guards (pass
/// at b096951, mutation-checked): the R4 <c>instantiates-canonical</c> match and, in the version matrix, the unversioned
/// queries, <c>q|2.0</c> and <c>:missing=false</c>. The <c>:StructureDefinition</c> rows and the type-modifier refusal fail at
/// 78d8b45, where a modifier left the canonicals unmatched. Chains and includes are in <see cref="CanonicalReferenceFollowTests"/>.
/// </summary>
public class CanonicalReferenceSearchTests
{
    private const string KbvPatient = "https://fhir.kbv.de/StructureDefinition/KBV_PR_Base_Patient";

    private const string KbvObservation = "https://fhir.kbv.de/StructureDefinition/KBV_PR_Base_Observation";

    private static readonly VersionedCorpus R5 = VersionedCorpus.For(FhirVersion.R5);

    private static readonly ISearchIndexer Indexer = new CanonicalReferenceSearchIndexer(R5.Indexer, R5.Definitions);

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
        var indexer = new CanonicalReferenceSearchIndexer(r4.Indexer, r4.Definitions);

        r4.Match(
                indexer,
                "CarePlan",
                "instantiates-canonical=http://example.org/PlanDefinition/pd",
                """{"resourceType":"CarePlan","id":"c","status":"active","intent":"plan","subject":{"reference":"Patient/p"},"instantiatesCanonical":["http://example.org/PlanDefinition/pd|1"]}""",
                """{"resourceType":"CarePlan","id":"other","status":"active","intent":"plan","subject":{"reference":"Patient/p"},"instantiatesCanonical":["http://example.org/PlanDefinition/pd2"]}""")
            .ShouldBe(["c"]);
    }

    [Theory]
    [InlineData("_profile:StructureDefinition=http://example.org/fhir/StructureDefinition/p", "typed,typed-v2")]
    [InlineData("_profile:StructureDefinition=http://example.org/fhir/StructureDefinition/p|2.0", "typed-v2")]
    [InlineData("_profile:StructureDefinition=http://example.org/profiles/q", "untyped,untyped-v1")]
    [InlineData("_profile:StructureDefinition=" + KbvPatient, "kbv-patient")]
    public void GivenATypeModifierNamingTheTarget_WhenSearched_ThenTheCanonicalIsMatchedByItsUrl(string query, string expected)
    {
        R5.Match(Indexer, "Patient", query, Profiled).ShouldBe(expected.Split(','));
    }

    /// <summary>
    /// The binder only accepts a type modifier naming a target, and lowering refuses every other modifier, so this one is
    /// built directly: a type the parameter does not target is refused rather than matching no canonical.
    /// </summary>
    [Fact]
    public void GivenATypeModifierNotNamingATarget_WhenCompiled_ThenItIsRefusedRatherThanMatchingNothing()
    {
        R5.Definitions.TryGetSearchParameter("Patient", "_profile", out SearchParameterInfo profile).ShouldBeTrue();
        var predicate = new SearchParameterPredicateExpression(
            profile,
            SearchComparator.Eq,
            new SearchModifier(SearchModifierCode.Type, "Patient"),
            new ReferenceSearchValueParser(R5.Schema, NullFhirBaseUriProvider.Instance).Parse("http://example.org/profiles/q"));
        var interpreter = new SearchQueryInterpreter();

        Should.Throw<SearchOperationNotSupportedException>(() => Expression.SearchParameter(profile, predicate).AcceptVisitor(interpreter, interpreter.InitialContext))
            .Issues.ShouldHaveSingleItem().Diagnostics.ShouldNotBeNull().ShouldContain("not supported for canonical references");
    }

    private static string Patient(string id, string profile) =>
        $$$"""{"resourceType":"Patient","id":"{{{id}}}","meta":{"profile":["{{{profile}}}"]}}""";
}
