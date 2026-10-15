// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using static Ignixa.Search.Tests.InMemory.SearchCorpus;

namespace Ignixa.Search.Tests.InMemory;

/// <summary>
/// What <see cref="Search.InMemory.SearchQueryInterpreter"/> matches. Every case here returned a wrong result or threw
/// before the interpreter compiled each parameter into a test of one index entry: multi-field criteria must be met by
/// one entry, comparisons follow the lowered field, and range bounds follow the FHIR prefix table.
/// </summary>
public class SearchQueryInterpreterMatchingTests
{
    private const string TwoCodings = """{"resourceType":"Observation","id":"mixed","status":"final","code":{"coding":[{"system":"http://loinc.org","code":"1234-5"},{"system":"http://snomed.info/sct","code":"8867-4"}]}}""";

    private const string HeartRate = """{"resourceType":"Observation","id":"hr","status":"final","code":{"coding":[{"system":"http://loinc.org","code":"8867-4"}]}}""";

    [Theory]
    [InlineData("code=http://loinc.org|8867-4", "hr")]
    [InlineData("code=http://snomed.info/sct|8867-4", "mixed")]
    [InlineData("code=http://snomed.info/sct|1234-5", "")]
    [InlineData("code:not=http://loinc.org|8867-4", "mixed")]
    public void GivenCodingsFromDifferentSystems_WhenSearchedBySystemAndCode_ThenBothMustComeFromTheSameCoding(string query, string expected)
    {
        Match("Observation", query, TwoCodings, HeartRate).ShouldBe(Ids(expected));
    }

    [Theory]
    [InlineData("identifier=http://hospital/mrn|555", "b")]
    [InlineData("identifier:not=http://hospital/mrn|555", "a")]
    public void GivenTwoIdentifiers_WhenSearchedBySystemAndValue_ThenBothMustComeFromTheSameIdentifier(string query, string expected)
    {
        Match(
            "Patient",
            query,
            """{"resourceType":"Patient","id":"a","identifier":[{"system":"http://hospital/mrn","value":"111"},{"system":"http://hl7.org/fhir/sid/us-ssn","value":"555"}]}""",
            """{"resourceType":"Patient","id":"b","identifier":[{"system":"http://hospital/mrn","value":"555"}]}""")
            .ShouldBe(Ids(expected));
    }

    [Theory]
    [InlineData("code-value-quantity=http://loinc.org|8867-4$gt60", "hr")]
    [InlineData("code-value-quantity=http://snomed.info/sct|8867-4$gt60", "mixed")]
    [InlineData("code-value-quantity=8867-4$gt60", "mixed,hr")]
    [InlineData("code-value-quantity=http://loinc.org|8867-4$lt90", "")]
    public void GivenMultiCodedCompositeComponent_WhenSearched_ThenEachComponentIsMatchedByOneOfItsValues(string query, string expected)
    {
        Match(
            "Observation",
            query,
            """{"resourceType":"Observation","id":"mixed","status":"final","code":{"coding":[{"system":"http://loinc.org","code":"1234-5"},{"system":"http://snomed.info/sct","code":"8867-4"}]},"valueQuantity":{"value":70,"system":"http://unitsofmeasure.org","code":"/min"}}""",
            """{"resourceType":"Observation","id":"hr","status":"final","code":{"coding":[{"system":"http://loinc.org","code":"8867-4"}]},"valueQuantity":{"value":100,"system":"http://unitsofmeasure.org","code":"/min"}}""")
            .ShouldBe(Ids(expected));
    }

    [Theory]
    [InlineData("date=gt2021-01-01T00:15:00Z", true)]
    [InlineData("date=ge2021-01-01T00:30:00Z", true)]
    [InlineData("date=eq2021-01-01T00:00:00Z", false)]
    [InlineData("date=ne2021-01-01T00:00:00Z", true)]
    [InlineData("date=sa2020-12-31T23:59:00Z", true)]
    [InlineData("date=eb2021-01-01T00:20:00Z", false)]
    [InlineData("date=gt2021-01-01T01:00:00Z", false)]
    [InlineData("date=lt2021-01-01T00:15:00Z", true)]
    [InlineData("date=sa2021-01-01T00:10:00Z", false)]
    public void GivenAPeriod_WhenSearchedWithDatePrefixes_ThenTheBoundTheFhirPrefixTableNamesIsCompared(string query, bool matches)
    {
        string[] matched = Match(
            "Encounter",
            query,
            """{"resourceType":"Encounter","id":"e1","status":"finished","class":{"code":"AMB"},"period":{"start":"2021-01-01T00:00:00Z","end":"2021-01-01T00:30:00Z"}}""");

        matched.ShouldBe(matches ? ["e1"] : []);
    }

    [Theory]
    [InlineData("probability=sa0.3", false)]
    [InlineData("probability=gt0.3", true)]
    [InlineData("probability=eb0.6", true)]
    [InlineData("probability=lt0.3", true)]
    public void GivenARange_WhenSearchedWithNumberPrefixes_ThenTheLowOrHighBoundTheTableNamesIsCompared(string query, bool matches)
    {
        // Built by hand: the stock Range-to-number converter reads Range.low as a scalar and so never indexes a Range.
        Definitions.TryGetSearchParameter("RiskAssessment", "probability", out SearchParameterInfo probability).ShouldBeTrue();
        (ResourceKey, IReadOnlyCollection<SearchIndexEntry>) range =
            (new ResourceKey("RiskAssessment", "r1"), [new SearchIndexEntry(probability, new NumberSearchValue(0.2m, 0.5m))]);

        Match("RiskAssessment", query, [range]).ShouldBe(matches ? ["r1"] : []);
    }

    [Theory]
    [InlineData("value-quantity=72|http://unitsofmeasure.org|/min", "o1")]
    [InlineData("value-quantity=72||/min", "o1")]
    [InlineData("value-quantity=72|http://unitsofmeasure.org|mm[Hg]", "")]
    public void GivenQuantityWithSystemAndCode_WhenSearched_ThenValueSystemAndCodeAreMatchedTogether(string query, string expected)
    {
        Match(
            "Observation",
            query,
            """{"resourceType":"Observation","id":"o1","status":"final","code":{"text":"x"},"valueQuantity":{"value":72,"system":"http://unitsofmeasure.org","code":"/min"}}""")
            .ShouldBe(Ids(expected));
    }

    [Theory]
    [InlineData("subject=Patient/p1", "o1")]
    [InlineData("subject=p1", "o1")]
    [InlineData("subject:Patient=p1", "o1")]
    [InlineData("subject=Patient/p2", "")]
    [InlineData("subject=Group/p1", "")]
    public void GivenReference_WhenSearched_ThenTypeAndIdAreMatched(string query, string expected)
    {
        Match(
            "Observation",
            query,
            """{"resourceType":"Observation","id":"o1","status":"final","code":{"text":"x"},"subject":{"reference":"Patient/p1"}}""")
            .ShouldBe(Ids(expected));
    }

    [Theory]
    [InlineData("url=http://example.org/vs/a", "a")]
    [InlineData("url:below=http://example.org/vs", "a,b")]
    public void GivenUri_WhenSearched_ThenTheUriIsMatched(string query, string expected)
    {
        Match(
            "ValueSet",
            query,
            """{"resourceType":"ValueSet","id":"a","status":"active","url":"http://example.org/vs/a"}""",
            """{"resourceType":"ValueSet","id":"b","status":"active","url":"http://example.org/vs/b"}""")
            .ShouldBe(Ids(expected));
    }

    [Fact]
    public void GivenTokenTextModifier_WhenSearched_ThenTheTextIsMatchedNotTheCode()
    {
        Match(
            "Observation",
            "code:text=heart",
            """{"resourceType":"Observation","id":"t1","status":"final","code":{"coding":[{"system":"http://loinc.org","code":"8867-4"}],"text":"Heart rate"}}""",
            """{"resourceType":"Observation","id":"t2","status":"final","code":{"coding":[{"system":"http://loinc.org","code":"heart"}]}}""")
            .ShouldBe(["t1"]);
    }

    [Theory]
    [InlineData("family=odegaard")]
    [InlineData("family=Ödegaard")]
    [InlineData("family:contains=DEGA")]
    public void GivenAccentedName_WhenSearchedWithoutAccents_ThenItMatches(string query)
    {
        Match("Patient", query, """{"resourceType":"Patient","id":"p3","name":[{"family":"Ödegaard"}]}""").ShouldBe(["p3"]);
    }

    [Theory]
    [InlineData("code=|555", "nosys")]
    [InlineData("code:not=|555", "sys,both")]
    [InlineData("code=|2", "both")]
    [InlineData("code=|1", "")]
    [InlineData("code=|555,http://s|1", "nosys,both")]
    public void GivenEmptySystem_WhenTokenSearched_ThenOnlyCodingsWithoutASystemMatch(string query, string expected)
    {
        Match(
            "Observation",
            query,
            """{"resourceType":"Observation","id":"nosys","status":"final","code":{"coding":[{"code":"555"}]}}""",
            """{"resourceType":"Observation","id":"sys","status":"final","code":{"coding":[{"system":"http://s","code":"555"}]}}""",
            """{"resourceType":"Observation","id":"both","status":"final","code":{"coding":[{"system":"http://s","code":"1"},{"code":"2"}]}}""")
            .ShouldBe(Ids(expected));
    }

    [Theory]
    [InlineData("_id=p1", "p1")]
    [InlineData("_id=p1,p3", "p1,p3")]
    [InlineData("_id:not=p1", "p2,p3")]
    [InlineData("_id=nope", "")]
    public void GivenIds_WhenSearchedById_ThenTheResourceKeyIsMatched(string query, string expected)
    {
        Match(
            "Patient",
            query,
            """{"resourceType":"Patient","id":"p1"}""",
            """{"resourceType":"Patient","id":"p2"}""",
            """{"resourceType":"Patient","id":"p3"}""")
            .ShouldBe(Ids(expected));
    }

    private static string[] Ids(string expected) => expected.Length == 0 ? [] : expected.Split(',');
}
