// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using static Ignixa.SqlOnFhir.Fabric.Tests.SilverProjectionHarness;
using static Ignixa.SqlOnFhir.Fabric.Tests.SyntheticSilverRoot;

namespace Ignixa.SqlOnFhir.Fabric.Tests;

/// <summary>
/// The top-level <c>identifier</c> column: source identifiers, deduplicated, plus one recording the source id, unless an
/// identifier already carries a fhirId coding.
/// </summary>
public class FabricSilverProjectionIdentifierTests
{
    /// <summary>An assigner written by the projection for an identifier that had none: a Reference with nothing in it.</summary>
    private static readonly string EmptyAssigner = ExpectedReference(ExpectedIdentifier());

    /// <summary>A missing assigner in a source identifier: an all-null Reference with an all-null identifier.</summary>
    private static readonly string MissingAssigner = ExpectedReference(AllNullIdentifier);

    private static readonly string SourceIdIdentifier =
        ExpectedIdentifier(value: "p1", system: Namespace, type: FhirIdConcept, assigner: EmptyAssigner);

    [Theory]
    [InlineData("""{"resourceType":"Patient","id":"p1"}""")]
    [InlineData("""{"resourceType":"Patient","id":"p1","identifier":null}""")]
    [InlineData("""{"resourceType":"Patient","id":"p1","identifier":[]}""")]
    public async Task GivenNoIdentifiers_WhenProjected_ThenTheSourceIdIdentifierIsTheOnlyOne(string json)
    {
        var row = (await Identifiers()).Project(json);

        row["identifier"].ShouldBeJson($"[{SourceIdIdentifier}]");
        row["identifier_string"].GetString().ShouldBe($"[{SourceIdIdentifier}]");
    }

    [Fact]
    public async Task GivenSourceIdentifiers_WhenProjected_ThenTheSourceIdIdentifierIsAppended()
    {
        var row = (await Identifiers()).Project(
            """{"resourceType":"Patient","id":"p1","identifier":[{"use":"usual","system":"urn:mrn","value":"7"}]}""");

        var source = ExpectedIdentifier(value: "7", system: "urn:mrn", use: "usual", type: "null", period: "null", assigner: MissingAssigner);
        row["identifier"].ShouldBeJson($"[{source},{SourceIdIdentifier}]");
    }

    [Fact]
    public async Task GivenDuplicateIdentifiers_WhenTheSourceIdIdentifierIsAppended_ThenTheFirstOfEachIsKept()
    {
        var row = (await Identifiers()).Project("""
            {"resourceType":"Patient","id":"p1","identifier":[
              {"system":"a","value":"2"},{"system":"a","value":"1"},{"value":"2","system":"a"},
              {"system":"a","value":"1","type":{"text":"T"}},{"system":"a","value":"1","type":{"text":"T","coding":[]}}]}
            """);

        var values = row["identifier"].EnumerateArray().Select(item => item.GetProperty("value").GetString()).ToArray();
        values.ShouldBe(["2", "1", "1", "p1"]);
        row["identifier"][2].GetProperty("type").ShouldBeJson("""{"extension":null,"id":null,"coding":[],"text":"T"}""");
    }

    [Fact]
    public async Task GivenManyDistinctIdentifiers_WhenDeduplicated_ThenTheWorkStaysLinear()
    {
        // 131,072 identifiers with 65,536 distinct values. A pairwise comparison needed minutes at this size; the
        // bound is ~10x the linear time measured on a developer machine and far below the quadratic time.
        const int Count = 131_072;
        const int Distinct = Count / 2;
        var projection = await Identifiers();
        var items = string.Join(',', Enumerable.Range(0, Count).Select(index => $$"""{"system":"s","value":"{{index % Distinct}}"}"""));

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var row = projection.Project($$"""{"resourceType":"Patient","id":"p1","identifier":[{{items}}]}""");
        stopwatch.Stop();

        var identifiers = row["identifier"];
        identifiers.GetArrayLength().ShouldBe(Distinct + 1);
        identifiers[0].GetProperty("value").GetString().ShouldBe("0");
        identifiers[Distinct - 1].GetProperty("value").GetString().ShouldBe((Distinct - 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
        identifiers[Distinct].GetProperty("value").GetString().ShouldBe("p1");
        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task GivenIdentifiersDifferingOnlyInAnIgnoredProperty_WhenDeduplicated_ThenTheyAreEqual()
    {
        var row = (await Identifiers()).Project("""
            {"resourceType":"Patient","id":"p1","identifier":[{"value":"1","id":"a"},{"value":"1","id":"b","_value":{"id":"x"}}]}
            """);

        row["identifier"].GetArrayLength().ShouldBe(2);
    }

    [Fact]
    public async Task GivenIdentifiersDifferingInTheirAssigner_WhenDeduplicated_ThenBothAreKept()
    {
        var row = (await Identifiers()).Project("""
            {"resourceType":"Patient","id":"p1","identifier":[
              {"value":"1","assigner":{"display":"A"}},{"value":"1","assigner":{"display":"B"}},{"value":"1","assigner":{"display":"A","identifier":null}}]}
            """);

        row["identifier"].EnumerateArray().Select(item => item.GetProperty("assigner").GetProperty("display").GetString())
            .ShouldBe(["A", "B", null]);
    }

    [Theory]
    [InlineData("""{"extension":"e"}""")]
    [InlineData("""{"id":"i"}""")]
    [InlineData("""{"display":"d"}""")]
    [InlineData("""{"reference":"#c"}""")]
    [InlineData("""{"type":"Organization"}""")]
    [InlineData("""{"identifier":{"extension":"e"}}""")]
    [InlineData("""{"identifier":{"use":"u"}}""")]
    [InlineData("""{"identifier":{"type":{"text":"t"}}}""")]
    [InlineData("""{"identifier":{"value":"v"}}""")]
    [InlineData("""{"identifier":{"system":"s"}}""")]
    [InlineData("""{"identifier":{"period":{"start":"2001"}}}""")]
    [InlineData("""{"identifier":{"assigner":{"display":"a"}}}""")]
    public async Task GivenAssignersDifferingInOneMember_WhenDeduplicated_ThenBothAreKept(string assigner)
    {
        var row = (await Identifiers()).Project($$$"""
            {"resourceType":"Patient","id":"p1","identifier":[{"value":"1","assigner":{}},{"value":"1","assigner":{{{assigner}}}}]}
            """);

        row["identifier"].GetArrayLength().ShouldBe(3);
    }

    [Fact]
    public async Task GivenAssignersEqualOnlyAfterNormalization_WhenDeduplicated_ThenBothAreKept()
    {
        var row = (await Identifiers()).Project("""
            {"resourceType":"Patient","id":"p1","identifier":[
              {"value":"1","assigner":{"reference":"Organization/o1"}},{"value":"1","assigner":{"reference":"Organization/o1","type":"Organization"}}]}
            """);

        row["identifier"].GetArrayLength().ShouldBe(3);
    }

    [Fact]
    public async Task GivenAssignersDifferingOnlyByAnEmptyIdentifier_WhenDeduplicated_ThenTheFirstIsKept()
    {
        var row = (await Identifiers()).Project("""
            {"resourceType":"Patient","id":"p1","identifier":[{"value":"1","assigner":{"display":"A"}},{"value":"1","assigner":{"display":"A","identifier":{}}}]}
            """);

        row["identifier"].GetArrayLength().ShouldBe(2);
    }

    [Fact]
    public async Task GivenAnIdentifierWithAFhirIdCoding_WhenProjected_ThenNothingIsAppendedOrDeduplicated()
    {
        var row = (await Identifiers()).Project($$$"""
            {"resourceType":"Patient","id":"p1","identifier":[
              {"value":"x","type":{"coding":[{"system":"{{{FhirIdSystem}}}","code":"fhirId"}]}},{"value":"d"},{"value":"d"}]}
            """);

        row["identifier"].EnumerateArray().Select(item => item.GetProperty("value").GetString()).ShouldBe(["x", "d", "d"]);
    }

    [Fact]
    public async Task GivenAnIdentifierAssigner_WhenProjected_ThenItIsANormalizedReference()
    {
        var row = (await Identifiers()).Project(
            """{"resourceType":"Patient","id":"p1","identifier":[{"value":"1","assigner":{"reference":"Organization/o1"}}]}""");

        var assigner = row["identifier"][0].GetProperty("assigner");
        assigner.GetProperty("id").GetString().ShouldBe(Hash(StrippedNamespace, "Organization", "o1"));
        assigner.GetProperty("msftSourceReference").GetString().ShouldBe("Organization/o1");
        assigner.GetProperty("identifier").GetProperty("value").GetString().ShouldBe("o1");
    }

    [Fact]
    public async Task GivenAnIdentifierElementWithoutAssigner_WhenProjected_ThenTheSourceIdIdentifierHasOnlyItsFields()
    {
        var projection = await CreateAsync("Patient", Field("identifier", ArrayOf(Strings("value", "system"))));

        var row = projection.Project("""{"resourceType":"Patient","id":"p1","identifier":[{"value":"1"},{"value":"1"}]}""");

        row["identifier"].ShouldBeJson($$"""[{"value":"1","system":null},{"value":"p1","system":{{Text(Namespace)}}}]""");
    }

    [Theory]
    [InlineData("{}", "identifier")]
    [InlineData("\"x\"", "identifier")]
    [InlineData("[{}, 5]", "identifier[1]")]
    [InlineData("[null]", "identifier[0]")]
    public async Task GivenMalformedIdentifiers_WhenProjected_ThenRejectedAtTheirPath(string identifiers, string path)
    {
        var projection = await Identifiers();

        projection.Rejection($$"""{"resourceType":"Patient","id":"p1","identifier":{{identifiers}}}""").Path.ShouldBe(path);
    }

    private static Task<FabricSilverProjection> Identifiers() => CreateAsync(
        "Patient",
        Field("identifier", ArrayOf(Identifier(Reference()))),
        Field("identifier_string", "string"));
}
