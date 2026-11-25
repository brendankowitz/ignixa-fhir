// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using static Ignixa.SqlOnFhir.Fabric.Tests.SilverProjectionHarness;
using static Ignixa.SqlOnFhir.Fabric.Tests.SyntheticSilverRoot;

namespace Ignixa.SqlOnFhir.Fabric.Tests;

/// <summary>
/// Reference normalization. Every struct whose type carries <c>msftSourceReference</c> is rewritten: a resolvable
/// literal reference in the source namespace gets the hashed Silver id and a source-id identifier, a logical reference
/// fills the identifier from its query, and the original text is kept in <c>msftSourceReference</c>.
/// </summary>
public class FabricSilverProjectionReferenceTests
{
    private static readonly string OrganizationO1 = Hash(StrippedNamespace, "Organization", "o1");

    [Fact]
    public async Task GivenNoScalarReference_WhenProjected_ThenTheCellIsAnAllNullStructWithAnAllNullIdentifier()
    {
        var row = (await References()).Project("""{"resourceType":"Patient","id":"p1"}""");

        var expected = ExpectedReference(AllNullIdentifier);
        row["managingOrganization"].ShouldBeJson(expected);
        row["managingOrganization_string"].GetString().ShouldBe(expected);
    }

    [Fact]
    public async Task GivenANullScalarReference_WhenProjected_ThenItIsTreatedAsAbsent()
    {
        var row = (await References()).Project("""{"resourceType":"Patient","id":"p1","managingOrganization":null}""");

        row["managingOrganization"].ShouldBeJson(ExpectedReference(AllNullIdentifier));
    }

    [Fact]
    public async Task GivenNoReferenceArray_WhenProjected_ThenTheCellAndCompanionAreNull()
    {
        var row = (await References()).Project("""{"resourceType":"Patient","id":"p1"}""");

        row["generalPractitioner"].ValueKind.ShouldBe(JsonValueKind.Null);
        row["generalPractitioner_string"].ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task GivenAnEmptyReferenceArray_WhenProjected_ThenTheCellIsEmpty()
    {
        var row = (await References()).Project("""{"resourceType":"Patient","id":"p1","generalPractitioner":[]}""");

        row["generalPractitioner"].ShouldBeJson("[]");
        row["generalPractitioner_string"].GetString().ShouldBe("[]");
    }

    [Theory]
    [InlineData("Organization/o1")]
    [InlineData("https://example.org/fhir/Organization/o1")]
    [InlineData("http://example.org/fhir/Organization/o1")]
    [InlineData("Organization/o1/_history/3")]
    [InlineData("relative/path/Organization/o1")]
    [InlineData("Patient?identifier=s|v/Organization/o1")]
    [InlineData("http://a b/Organization/o1")]
    [InlineData("http://xOrganization/o1")]
    public async Task GivenALiteralReferenceInTheSourceNamespace_WhenProjected_ThenItGetsTheSilverIdAndASourceIdIdentifier(string reference)
    {
        var cell = await ManagingOrganization($$"""{"reference":"{{reference}}","display":"Acme"}""");

        cell.ShouldBeJson(ExpectedReference(
            ExpectedIdentifier(value: "o1", system: Namespace, type: FhirIdConcept),
            source: reference,
            type: "Organization",
            id: OrganizationO1,
            idOrig: "o1",
            display: "Acme"));
    }

    [Fact]
    public async Task GivenALiteralReferenceWithAMultiSegmentBase_WhenTheBaseIsLocal_ThenTheLongestBaseIsUsed()
    {
        var cell = await ManagingOrganization(
            """{"reference":"https://example.org/a/b/Organization/o1"}""",
            "https://example.org/a/b/");

        cell.GetProperty("id").GetString().ShouldBe(Hash("example.org/a/b", "Organization", "o1"));
    }

    [Theory]
    [InlineData("https://other.example.org/fhir/Organization/o1", "Organization")]
    [InlineData("http://other-host/Organization/o1", "Organization")]
    [InlineData("https://https://example.org/fhir/Organization/o1", "Organization")]
    [InlineData("http://example.org/fhir/Organization/o1/Patient/p2", "Patient")]
    public async Task GivenALiteralReferenceWithAForeignBase_WhenProjected_ThenOnlyTheTypeIsResolved(string reference, string type)
    {
        var cell = await ManagingOrganization($$"""{"reference":"{{reference}}"}""");

        cell.ShouldBeJson(ExpectedReference(
            ExpectedIdentifier(type: NullConcept),
            source: reference,
            type: type));
    }

    [Theory]
    [InlineData("a", "/1", null)]
    [InlineData("a/", "", null)]
    [InlineData("https://", "Organization/o1", "Organization")]
    [InlineData("http://a", "", null)]
    [InlineData("http://a/", "Organization/o1", "Organization")]
    [InlineData("Patient", "?identifier=s|v", "Patient")]
    [InlineData("https://h/Patient", "", null)]
    public async Task GivenAMegabyteReference_WhenProjected_ThenMatchingStaysLinear(string unit, string tail, string? type)
    {
        // Rescanning from every start position made these minutes long at 1 MB. The bound is ~10x the linear time
        // measured on a developer machine and far below the quadratic time, so it is not timing-sensitive.
        var reference = string.Concat(Enumerable.Repeat(unit, (1 << 20) / unit.Length)) + tail;
        var projection = await References();
        var json = $$$"""{"resourceType":"Patient","id":"p1","managingOrganization":{"reference":"{{{reference}}}"}}""";

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var cell = projection.Project(json)["managingOrganization"];
        stopwatch.Stop();

        cell.GetProperty("msftSourceReference").GetString().ShouldBe(reference);
        cell.GetProperty("type").GetString().ShouldBe(type);
        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task GivenADeclaredType_WhenProjected_ThenItOverridesTheParsedTypeIncludingInTheId()
    {
        var cell = await ManagingOrganization("""{"reference":"Organization/o1","type":"Group"}""");

        cell.GetProperty("type").GetString().ShouldBe("Group");
        cell.GetProperty("id").GetString().ShouldBe(Hash(StrippedNamespace, "Group", "o1"));
        cell.GetProperty("idOrig").GetString().ShouldBe("o1");
    }

    [Fact]
    public async Task GivenAnEmptyDeclaredType_WhenProjected_ThenTheParsedTypeIsUsed()
    {
        var cell = await ManagingOrganization("""{"reference":"Organization/o1","type":""}""");

        cell.GetProperty("type").GetString().ShouldBe("Organization");
        cell.GetProperty("id").GetString().ShouldBe(OrganizationO1);
    }

    [Fact]
    public async Task GivenAnIdentifierWithAValue_WhenALiteralReferenceResolves_ThenTheIdentifierIsKept()
    {
        var cell = await ManagingOrganization("""{"reference":"Organization/o1","identifier":{"use":"usual","value":"x"}}""");

        cell.GetProperty("identifier").ShouldBeJson(ExpectedIdentifier(value: "x", use: "usual"));
        cell.GetProperty("id").GetString().ShouldBe(OrganizationO1);
    }

    [Fact]
    public async Task GivenAnIdentifierWithOnlyASystem_WhenALiteralReferenceResolves_ThenTheIdentifierIsKept()
    {
        var cell = await ManagingOrganization("""{"reference":"Organization/o1","identifier":{"system":"urn:s"}}""");

        cell.GetProperty("identifier").ShouldBeJson(ExpectedIdentifier(system: "urn:s"));
    }

    [Fact]
    public async Task GivenAnIdentifierWithAFhirIdCoding_WhenALiteralReferenceResolves_ThenTheIdentifierIsKept()
    {
        var cell = await ManagingOrganization(
            """{"reference":"Organization/o1","identifier":{"type":{"coding":[{"code":"fhirId"}]}}}""");

        cell.GetProperty("identifier").ShouldBeJson(ExpectedIdentifier(type: Concept(null, Coding(null, "fhirId"))));
    }

    [Fact]
    public async Task GivenAnIdentifierTypeWithoutAFhirIdCoding_WhenALiteralReferenceResolves_ThenTheCodingIsAppended()
    {
        var cell = await ManagingOrganization(
            """{"reference":"Organization/o1","identifier":{"type":{"text":"T","coding":[{"system":"urn:c","code":"MR","userSelected":true}]}}}""");

        cell.GetProperty("identifier").ShouldBeJson(ExpectedIdentifier(
            value: "o1",
            system: Namespace,
            type: Concept("T", Coding("urn:c", "MR", userSelected: "true"), FhirIdCoding)));
    }

    [Fact]
    public async Task GivenAnIdentifierTypeWithoutCodings_WhenALiteralReferenceResolves_ThenTheCodingIsTheOnlyOne()
    {
        var cell = await ManagingOrganization("""{"reference":"Organization/o1","identifier":{"type":{"text":"T"}}}""");

        cell.GetProperty("identifier").ShouldBeJson(ExpectedIdentifier(value: "o1", system: Namespace, type: Concept("T", FhirIdCoding)));
    }

    [Fact]
    public async Task GivenAnEmptyIdentifierValue_WhenALiteralReferenceResolves_ThenTheSourceIdIsAdded()
    {
        var cell = await ManagingOrganization("""{"reference":"Organization/o1","identifier":{"value":"","system":""}}""");

        cell.GetProperty("identifier").ShouldBeJson(ExpectedIdentifier(value: "o1", system: Namespace, type: FhirIdConcept));
    }

    [Fact]
    public async Task GivenALogicalReferenceWithoutBaseOrSystem_WhenProjected_ThenTheSourceNamespaceBecomesTheSystem()
    {
        const string Reference = "Organization?identifier=abc";

        var cell = await ManagingOrganization($$$"""{"reference":"{{{Reference}}}","identifier":{"use":"official","value":"old"}}""");

        cell.ShouldBeJson(ExpectedReference(
            ExpectedIdentifier(value: "abc", system: Namespace, use: "official"),
            source: Reference,
            type: "Organization"));
    }

    [Fact]
    public async Task GivenALogicalReferenceWithAnEmptySystem_WhenProjected_ThenTheSourceNamespaceBecomesTheSystem()
    {
        var cell = await ManagingOrganization("""{"reference":"Organization?identifier=|abc"}""");

        cell.GetProperty("identifier").ShouldBeJson(ExpectedIdentifier(value: "abc", system: Namespace));
    }

    [Theory]
    [InlineData("Organization?identifier=urn:sys|abc", "urn:sys", "abc")]
    [InlineData("Organization?identifier=a|b|c", "a|b", "c")]
    [InlineData("Organization?identifier=urn:sys|abc\nignored", "urn:sys", "abc")]
    [InlineData("Organization?identifier=urn:sys|", "urn:sys", "")]
    [InlineData("http://a Organization?identifier=urn:sys|abc", "urn:sys", "abc")]
    public async Task GivenALogicalReferenceWithASystem_WhenProjected_ThenTheQueryFillsTheIdentifier(string reference, string system, string value)
    {
        var cell = await ManagingOrganization($$"""{"reference":{{Text(reference)}}}""");

        cell.ShouldBeJson(ExpectedReference(ExpectedIdentifier(value: value, system: system), source: reference, type: "Organization"));
        cell.GetProperty("id").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Theory]
    [InlineData("https://example.org/Organization?identifier=urn:sys|abc", "urn:sys")]
    [InlineData("http://example.org/Organization?identifier=abc", null)]
    public async Task GivenALogicalReferenceWithTheLocalBase_WhenProjected_ThenTheQueryFillsTheIdentifierVerbatim(string reference, string? system)
    {
        var cell = await ManagingOrganization(
            $$$"""{"reference":"{{{reference}}}","identifier":{"value":"old","system":"old"}}""",
            "https://example.org");

        cell.GetProperty("identifier").ShouldBeJson(ExpectedIdentifier(value: "abc", system: system));
    }

    [Fact]
    public async Task GivenALogicalReferenceWithAForeignBase_WhenProjected_ThenTheIdentifierIsUnchanged()
    {
        const string Reference = "https://other.example.org/Organization?identifier=urn:sys|abc";

        var cell = await ManagingOrganization($$$"""{"reference":"{{{Reference}}}","identifier":{"value":"old"}}""");

        cell.ShouldBeJson(ExpectedReference(ExpectedIdentifier(value: "old"), source: Reference, type: "Organization"));
    }

    [Fact]
    public async Task GivenALogicalReferenceWithAMultiSegmentBase_WhenProjected_ThenItMatchesWithoutTheBase()
    {
        // Source-derived: the logical form allows a one-segment base only, and the pattern may start anywhere, so the
        // type and query match on their own and the reference is treated as local.
        var cell = await ManagingOrganization("""{"reference":"https://other.example.org/fhir/Organization?identifier=urn:sys|abc"}""");

        cell.GetProperty("identifier").ShouldBeJson(ExpectedIdentifier(value: "abc", system: "urn:sys"));
    }

    [Theory]
    [InlineData("#contained-1")]
    [InlineData("urn:uuid:5d3c9bba-3a19-4b39-9d3e-2c44bb1f8f4b")]
    [InlineData("Unknown/x1")]
    [InlineData("Resource/x1")]
    [InlineData("DomainResource/x1")]
    [InlineData("Organization/")]
    [InlineData("Organization?name=x")]
    [InlineData("http://example.org/fhir/")]
    public async Task GivenAnUnmatchedReference_WhenProjected_ThenOnlyTheSourceTextIsKept(string reference)
    {
        var cell = await ManagingOrganization($$"""{"id":"element-1","reference":"{{reference}}"}""");

        cell.ShouldBeJson(ExpectedReference(ExpectedIdentifier(), source: reference));
    }

    [Fact]
    public async Task GivenAnUnmatchedReferenceWithADeclaredType_WhenProjected_ThenTheDeclaredTypeIsKept()
    {
        var cell = await ManagingOrganization("""{"reference":"#c1","type":"Organization"}""");

        cell.ShouldBeJson(ExpectedReference(ExpectedIdentifier(), source: "#c1", type: "Organization"));
    }

    [Fact]
    public async Task GivenAReferenceEmbeddedInText_WhenProjected_ThenTheFirstMatchWins()
    {
        var cell = await ManagingOrganization("""{"reference":"see Organization/o1 and Organization/o2"}""");

        cell.GetProperty("idOrig").GetString().ShouldBe("o1");
    }

    [Fact]
    public async Task GivenAnIdLongerThan64Characters_WhenProjected_ThenTheFirst64AreTheId()
    {
        var longId = new string('a', 70);

        var cell = await ManagingOrganization($$"""{"reference":"Organization/{{longId}}"}""");

        cell.GetProperty("idOrig").GetString().ShouldBe(longId[..64]);
    }

    [Fact]
    public async Task GivenAnEmptyReference_WhenProjected_ThenItStaysEmpty()
    {
        var cell = await ManagingOrganization("""{"reference":"","type":"Organization"}""");

        cell.ShouldBeJson(ExpectedReference(ExpectedIdentifier(), source: string.Empty, type: "Organization", reference: string.Empty));
    }

    [Fact]
    public async Task GivenOnlyADisplay_WhenProjected_ThenTheIdentifierHasEmptyParts()
    {
        var cell = await ManagingOrganization("""{"display":"Acme"}""");

        cell.ShouldBeJson(ExpectedReference(ExpectedIdentifier(), display: "Acme"));
    }

    [Fact]
    public async Task GivenNonStringReferenceLeaves_WhenProjected_ThenTheyBecomeJsonText()
    {
        var cell = await ManagingOrganization(
            """{"reference":"#c1","display":5,"extension":[{"url":"u","valueString":"a\"b"}],"identifier":{"value":7,"use":true}}""");

        cell.GetProperty("display").GetString().ShouldBe("5");
        cell.GetProperty("extension").GetString().ShouldBe("""[{"url":"u","valueString":"a\"b"}]""");
        cell.GetProperty("identifier").GetProperty("value").GetString().ShouldBe("7");
        cell.GetProperty("identifier").GetProperty("use").GetString().ShouldBe("true");
    }

    [Fact]
    public async Task GivenANonStringReferenceText_WhenProjected_ThenItsJsonTextIsMatched()
    {
        var cell = await ManagingOrganization("""{"reference":12}""");

        cell.ShouldBeJson(ExpectedReference(ExpectedIdentifier(), source: "12"));
    }

    [Fact]
    public async Task GivenANestedAssigner_WhenProjected_ThenItIsShallowText()
    {
        var cell = await ManagingOrganization(
            """{"display":"x","identifier":{"assigner":{"reference":"Organization/a","display":"A","identifier":{"value":"v","system":"s"}}}}""");

        var assigner = """
            {"extension":null,"id":null,"reference":"Organization/a","type":null,
             "identifier":{"extension":null,"use":null,"type":null,"value":"v","system":"s","period":null,"assigner":null},"display":"A"}
            """;
        cell.GetProperty("identifier").ShouldBeJson(ExpectedIdentifier(assigner: assigner));
    }

    [Fact]
    public async Task GivenEveryNestedStringMember_WhenProjected_ThenEachKeepsItsOwnSlot()
    {
        var cell = await ManagingOrganization("""
            {"identifier":{"type":{"extension":"te","id":"ti","text":"tt","coding":[{"extension":"ce","id":"ci","system":"cs","version":"cv","code":"cc","display":"cd"}]},
             "assigner":{"extension":"ae","id":"ai","reference":"ar","type":"at","display":"ad",
               "identifier":{"extension":"ie","use":"iu","type":"it","value":"iv","system":"is","period":"ip","assigner":"ia"}}}}
            """);

        var identifier = cell.GetProperty("identifier");
        identifier.GetProperty("type").GetProperty("extension").GetString().ShouldBe("te");
        identifier.GetProperty("type").GetProperty("id").GetString().ShouldBe("ti");
        identifier.GetProperty("type").GetProperty("text").GetString().ShouldBe("tt");
        identifier.GetProperty("type").GetProperty("coding")[0].ShouldBeJson(
            """{"extension":"ce","id":"ci","system":"cs","version":"cv","code":"cc","display":"cd","userSelected":null}""");
        identifier.GetProperty("assigner").ShouldBeJson("""
            {"extension":"ae","id":"ai","reference":"ar","type":"at",
             "identifier":{"extension":"ie","use":"iu","type":"it","value":"iv","system":"is","period":"ip","assigner":"ia"},"display":"ad"}
            """);
    }

    [Fact]
    public async Task GivenANestedAssignerWithoutIdentifier_WhenProjected_ThenItsIdentifierIsAllNull()
    {
        var cell = await ManagingOrganization("""{"identifier":{"assigner":{"display":"A"}}}""");

        var assigner = """
            {"extension":null,"id":null,"reference":null,"type":null,
             "identifier":{"extension":null,"use":null,"type":null,"value":null,"system":null,"period":null,"assigner":null},"display":"A"}
            """;
        cell.GetProperty("identifier").ShouldBeJson(ExpectedIdentifier(assigner: assigner));
    }

    [Fact]
    public async Task GivenAnIdentifierPeriod_WhenProjected_ThenOnlyAnEmptyPeriodRemains()
    {
        var cell = await ManagingOrganization("""{"identifier":{"period":{"start":"2020-01-01"}}}""");

        cell.GetProperty("identifier").ShouldBeJson(ExpectedIdentifier());
    }

    [Theory]
    [InlineData("\"Organization/o1\"", "managingOrganization")]
    [InlineData("""{"identifier":[]}""", "managingOrganization.identifier")]
    [InlineData("""{"identifier":{"type":"MR"}}""", "managingOrganization.identifier.type")]
    [InlineData("""{"identifier":{"period":"2020"}}""", "managingOrganization.identifier.period")]
    [InlineData("""{"identifier":{"type":{"coding":{}}}}""", "managingOrganization.identifier.type.coding")]
    [InlineData("""{"identifier":{"type":{"coding":["MR"]}}}""", "managingOrganization.identifier.type.coding[0]")]
    [InlineData("""{"identifier":{"type":{"coding":[{"system":5}]}}}""", "managingOrganization.identifier.type.coding[0].system")]
    [InlineData("""{"identifier":{"type":{"coding":[{"userSelected":"yes"}]}}}""", "managingOrganization.identifier.type.coding[0].userSelected")]
    [InlineData("""{"identifier":{"type":{"extension":[{"url":"u"}]}}}""", "managingOrganization.identifier.type.extension")]
    [InlineData("""{"identifier":{"type":{"text":1}}}""", "managingOrganization.identifier.type.text")]
    [InlineData("""{"identifier":{"type":{"id":true}}}""", "managingOrganization.identifier.type.id")]
    [InlineData("""{"identifier":{"assigner":"Organization/a"}}""", "managingOrganization.identifier.assigner")]
    [InlineData("""{"identifier":{"assigner":{"display":{"x":1}}}}""", "managingOrganization.identifier.assigner.display")]
    [InlineData("""{"identifier":{"assigner":{"identifier":"x"}}}""", "managingOrganization.identifier.assigner.identifier")]
    [InlineData("""{"identifier":{"assigner":{"identifier":{"type":{"text":"T"}}}}}""", "managingOrganization.identifier.assigner.identifier.type")]
    public async Task GivenAValueTheSourcePipelineWouldNotStoreAsJson_WhenProjected_ThenRejectedAtItsPath(string reference, string path)
    {
        var projection = await References();

        var error = projection.Rejection($$"""{"resourceType":"Patient","id":"p1","managingOrganization":{{reference}}}""");

        error.Path.ShouldBe(path);
    }

    [Fact]
    public async Task GivenAReferenceArray_WhenProjected_ThenEachItemIsNormalized()
    {
        var row = (await References()).Project(
            """{"resourceType":"Patient","id":"p1","generalPractitioner":[{"reference":"Practitioner/pr1"},{"display":"Dr"}]}""");

        var expected = $"[{ExpectedReference(
            ExpectedIdentifier(value: "pr1", system: Namespace, type: FhirIdConcept),
            source: "Practitioner/pr1",
            type: "Practitioner",
            id: Hash(StrippedNamespace, "Practitioner", "pr1"),
            idOrig: "pr1")},{ExpectedReference(ExpectedIdentifier(), display: "Dr")}]";
        row["generalPractitioner"].ShouldBeJson(expected);
        row["generalPractitioner_string"].GetString().ShouldBe(expected);
    }

    [Fact]
    public async Task GivenANullItemInAReferenceArray_WhenProjected_ThenRejectedAtTheItem()
    {
        var projection = await References();

        projection.Rejection("""{"resourceType":"Patient","id":"p1","generalPractitioner":[{"display":"Dr"},null]}""")
            .Path.ShouldBe("generalPractitioner[1]");
    }

    [Fact]
    public async Task GivenAReferenceNestedInABackboneElement_WhenProjected_ThenItIsNormalized()
    {
        var projection = await CreateAsync(
            "Patient",
            Field("link", ArrayOf(Struct(Field("other", Reference()), Field("type", "string")))));

        var row = projection.Project("""{"resourceType":"Patient","id":"p1","link":[{"type":"seealso","other":{"reference":"Patient/p2"}}]}""");

        var link = row["link"][0];
        link.GetProperty("type").GetString().ShouldBe("seealso");
        link.GetProperty("other").GetProperty("id").GetString().ShouldBe(Hash(StrippedNamespace, "Patient", "p2"));
    }

    private static Task<FabricSilverProjection> References() => CreateAsync(
        "Patient",
        Field("managingOrganization", Reference()),
        Field("managingOrganization_string", "string"),
        Field("generalPractitioner", ArrayOf(Reference())),
        Field("generalPractitioner_string", "string"));

    private static async Task<JsonElement> ManagingOrganization(string reference, string sourceNamespace = Namespace)
    {
        var projection = await References();
        var row = projection.Project($$"""{"resourceType":"Patient","id":"p1","managingOrganization":{{reference}}}""", sourceNamespace);
        row["managingOrganization_string"].GetString().ShouldNotBeNull();
        return row["managingOrganization"];
    }
}
