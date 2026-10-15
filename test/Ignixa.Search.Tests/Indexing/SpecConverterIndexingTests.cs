// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Ignixa.Search.Indexing.Converters;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Search.Tests.Indexing;

/// <summary>
/// Regressions for the shared converters' index values (what the in-memory, FileSystem and SQL backends all store), each
/// checked against the search page's mapping for its type. Every row fails at 206143a except these guards, each
/// mutation-checked: the shared-indexer canonical rows (see their comment), <c>telecom=phone|...</c>, the quantity rows that name a system or no unit (<c>|ucum|[lb_av]</c>,
/// <c>|ucum|lbs</c>, <c>||[lb_av]</c>, <c>||kg</c>) and <c>probability=gt0.6</c>.
/// </summary>
public class SpecConverterIndexingTests
{
    private const string HomePhone =
        """{"resourceType":"Patient","id":"p1","telecom":[{"system":"phone","value":"555-1234","use":"home"}]}""";

    private const string WeightInPounds =
        """{"resourceType":"Observation","id":"w1","status":"final","code":{"text":"weight"},"valueQuantity":{"value":185,"unit":"lbs","system":"http://unitsofmeasure.org","code":"[lb_av]"}}""";

    private const string ProbabilityRange =
        """{"resourceType":"RiskAssessment","id":"r1","status":"final","subject":{"reference":"Patient/p1"},"prediction":[{"probabilityRange":{"low":{"value":0.2},"high":{"value":0.5}}}]}""";

    private const string UrnSubject =
        """{"resourceType":"Observation","id":"u1","status":"final","code":{"text":"x"},"subject":{"reference":"urn:uuid:5f0c4b1e-7b8e-4d6f-9c2a-0e1f2a3b4c5d"}}""";

    [Theory]
    [InlineData(FhirVersion.R4)]
    [InlineData(FhirVersion.R5)]
    public void GivenAContactPoint_WhenIndexed_ThenItsValueIsTheCodeWithNoSystem(FhirVersion version)
    {
        TokenSearchValue token = VersionedCorpus.For(version).Values(HomePhone, "telecom").ShouldHaveSingleItem().ShouldBeOfType<TokenSearchValue>();

        (token.System, token.Code).ShouldBe((null, "555-1234"));
    }

    [Theory]
    [InlineData("telecom=home|555-1234")]
    [InlineData("telecom=phone|555-1234")]
    public void GivenAContactPointWithAUse_WhenSearchedWithTheUseAsTheSystem_ThenItDoesNotMatch(string query)
    {
        VersionedCorpus.For(FhirVersion.R4).Match("Patient", query, HomePhone).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("value-quantity=185||lbs", "w1")]
    [InlineData("value-quantity=185||[lb_av]", "w1")]
    [InlineData("value-quantity=185|http://unitsofmeasure.org|[lb_av]", "w1")]
    [InlineData("value-quantity=185|http://unitsofmeasure.org|lbs", "")]
    [InlineData("value-quantity=185||kg", "")]
    public void GivenAQuantityWhoseUnitDiffersFromItsCode_WhenSearchedWithoutASystem_ThenEitherTheCodeOrTheUnitMatches(string query, string expected)
    {
        VersionedCorpus.For(FhirVersion.R4).Match("Observation", query, WeightInPounds).ShouldBe(Ids(expected));
    }

    [Fact]
    public void GivenAQuantityWhoseUnitDiffersFromItsCode_WhenIndexed_ThenTheUnitIsASecondValueWithNoSystem()
    {
        VersionedCorpus.For(FhirVersion.R4).Values(WeightInPounds, "value-quantity")
            .Cast<QuantitySearchValue>()
            .Select(quantity => (quantity.System, quantity.Code, quantity.Low, quantity.High))
            .ShouldBe([("http://unitsofmeasure.org", "[lb_av]", 185m, 185m), (null, "lbs", 185m, 185m)]);
    }

    [Theory]
    [InlineData("code-value-quantity=weight$185||lbs")]
    [InlineData("combo-code-value-quantity=weight$185||lbs")]
    public void GivenAQuantityUnderAComposite_WhenSearchedByItsUnit_ThenItMatches(string query)
    {
        VersionedCorpus.For(FhirVersion.R4).Match("Observation", query, WeightInPounds.Replace("""{"text":"weight"}""", """{"coding":[{"code":"weight"}]}""", StringComparison.Ordinal))
            .ShouldBe(["w1"]);
    }

    [Theory]
    [InlineData("probability=lt0.3", "r1")]
    [InlineData("probability=gt0.3", "r1")]
    [InlineData("probability=gt0.6", "")]
    public void GivenARiskAssessmentWithAProbabilityRange_WhenSearchedByNumber_ThenTheRangeIsIndexed(string query, string expected)
    {
        // R4 only: R5 and later narrow the expression to prediction.probability.ofType(decimal), so no Range is selected.
        VersionedCorpus.For(FhirVersion.R4).Match("RiskAssessment", query, ProbabilityRange).ShouldBe(Ids(expected));
    }

    [Fact]
    public void GivenARange_WhenConvertedToANumber_ThenItsBoundsAreTheLowAndHighValues()
    {
        IElement range = RangeOf("""{"low":{"value":0.2},"high":{"value":0.5}}""");

        NumberSearchValue number = new RangeToNumberSearchValueConverter().ConvertTo(range).ShouldHaveSingleItem().ShouldBeOfType<NumberSearchValue>();

        (number.Low, number.High).ShouldBe((0.2m, 0.5m));
    }

    [Fact]
    public void GivenARangeWithUnits_WhenConvertedToAQuantity_ThenItsBoundsSystemCodeAndUnitAreIndexed()
    {
        IElement range = RangeOf("""{"low":{"value":2,"unit":"milligram","system":"http://unitsofmeasure.org","code":"mg"},"high":{"value":5,"unit":"milligram","system":"http://unitsofmeasure.org","code":"mg"}}""");

        new RangeToQuantitySearchValueConverter().ConvertTo(range)
            .Cast<QuantitySearchValue>()
            .Select(quantity => (quantity.System, quantity.Code, quantity.Low, quantity.High))
            .ShouldBe([("http://unitsofmeasure.org", "mg", 2m, 5m), (null, "milligram", 2m, 5m)]);
    }

    [Fact]
    public void GivenAHighOnlyRange_WhenConvertedToAQuantity_ThenTheHighBoundCarriesTheSystemAndCode()
    {
        IElement range = RangeOf("""{"high":{"value":5,"system":"http://unitsofmeasure.org","code":"mg"}}""");

        QuantitySearchValue quantity = new RangeToQuantitySearchValueConverter().ConvertTo(range).ShouldHaveSingleItem().ShouldBeOfType<QuantitySearchValue>();

        (quantity.System, quantity.Code, quantity.Low, quantity.High).ShouldBe(("http://unitsofmeasure.org", "mg", (decimal?)null, (decimal?)5m));
    }

    [Theory]
    [InlineData(FhirVersion.R5, "Patient", """{"resourceType":"Patient","id":"p","meta":{"profile":["http://example.org/fhir/StructureDefinition/my-patient|1.0"]}}""", "_profile")]
    [InlineData(FhirVersion.R4, "CarePlan", """{"resourceType":"CarePlan","id":"c","status":"active","intent":"plan","subject":{"reference":"Patient/p"},"instantiatesCanonical":["http://example.org/PlanDefinition/pd"]}""", "instantiates-canonical")]
    public void GivenACanonicalUnderAReferenceParameter_WhenIndexedByTheSharedIndexer_ThenNothingIsIndexedForIt(FhirVersion version, string resourceType, string json, string code)
    {
        // Regression against b096951, which indexed these as references (so SQL wrote ReferenceSearchParam rows); a guard
        // against 206143a. Canonical storage is the open #430 work, so the shared indexer, and with it every backend's
        // rows, holds nothing for them; only CanonicalReferenceSearchIndexer adds them, in memory.
        VersionedCorpus corpus = VersionedCorpus.For(version);
        corpus.Definitions.TryGetSearchParameter(resourceType, code, out SearchParameterInfo parameter).ShouldBeTrue();
        parameter.Type.ShouldBe(SearchParamType.Reference);

        corpus.Values(json, code).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("subject=urn:uuid:5f0c4b1e-7b8e-4d6f-9c2a-0e1f2a3b4c5d", "u1")]
    [InlineData("subject:missing=false", "u1")]
    [InlineData("subject:missing=true", "")]
    public void GivenAUrnReference_WhenSearched_ThenItIsIndexedAsTheWholeUrn(string query, string expected)
    {
        VersionedCorpus.For(FhirVersion.R4).Match("Observation", query, UrnSubject).ShouldBe(Ids(expected));
    }

    private static IElement RangeOf(string rangeJson) =>
        VersionedCorpus.For(FhirVersion.R4)
            .Parse($$"""{"resourceType":"RiskAssessment","id":"r","status":"final","subject":{"reference":"Patient/p"},"prediction":[{"probabilityRange":{{rangeJson}}}]}""")
            .Select("RiskAssessment.prediction.probability")
            .ShouldHaveSingleItem();

    private static string[] Ids(string expected) => expected.Length == 0 ? [] : expected.Split(',');
}
