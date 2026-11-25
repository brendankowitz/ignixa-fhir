// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.Serialization.SourceNodes;
using static Ignixa.SqlOnFhir.Fabric.Tests.SilverProjectionHarness;
using static Ignixa.SqlOnFhir.Fabric.Tests.SyntheticSilverRoot;

namespace Ignixa.SqlOnFhir.Fabric.Tests;

/// <summary>
/// Text that is not valid Unicode is rejected anywhere in the resource, at its path, whether it arrives as a lone
/// surrogate escape in parsed JSON or as a lone surrogate in a resource built in code.
/// </summary>
public class FabricSilverProjectionUnicodeTests
{
    private const string Lone = "\ud800";

    // Case names stand in for the data, which holds lone surrogates the test runner could not serialize faithfully.
    private static readonly Dictionary<string, (Func<JsonObject> Build, string Path)> BuiltInCode = new()
    {
        ["high surrogate"] = (() => Patient(new() { ["gender"] = "\ud800" }), "gender"),
        ["low surrogate"] = (() => Patient(new() { ["gender"] = "\udc00" }), "gender"),
        ["reversed pair"] = (() => Patient(new() { ["gender"] = "\udc00\ud800" }), "gender"),
        ["high surrogate at the end"] = (() => Patient(new() { ["name"] = new JsonArray(new JsonObject { ["family"] = "x\ud83d" }) }), "name[0].family"),
        ["id"] = (() => new JsonObject { ["resourceType"] = "Patient", ["id"] = Lone }, "id"),
        ["nested property name"] = (() => Patient(new() { ["name"] = new JsonArray(new JsonObject { [Lone] = "x" }) }), "name[0]"),
        ["resource property name"] = (() => Patient(new() { [Lone] = 1 }), "Patient"),
        ["extension text"] = (() => Patient(new() { ["extension"] = new JsonArray(new JsonObject { ["url"] = Lone }) }), "extension[0].url"),
        ["unmapped property"] = (() => Patient(new() { ["unmapped"] = Lone }), "unmapped"),
        ["parsed resource property name"] = (() => JsonNode.Parse("""{"resourceType":"Patient","id":"p1","\ud800":1}""")!.AsObject(), "Patient"),
    };

    [Theory]
    [InlineData("""{"resourceType":"Patient","id":"\ud800"}""", "id")]
    [InlineData("""{"resourceType":"Patient","id":"p1","gender":"\ud800"}""", "gender")]
    [InlineData("""{"resourceType":"Patient","id":"p1","gender":"\udc00"}""", "gender")]
    [InlineData("""{"resourceType":"Patient","id":"p1","gender":"a\ud800b"}""", "gender")]
    [InlineData("""{"resourceType":"Patient","id":"p1","birthDate":"\ud800"}""", "birthDate")]
    [InlineData("""{"resourceType":"Patient","id":"p1","name":[{"family":"\ud800"}]}""", "name[0].family")]
    [InlineData("""{"resourceType":"Patient","id":"p1","name":[{"\ud800":"x","family":"a"}]}""", "name[0]")]
    [InlineData("""{"resourceType":"Patient","id":"p1","extension":[{"url":"u","valueString":"\ud800"}]}""", "extension[0].valueString")]
    [InlineData("""{"resourceType":"Patient","id":"p1","extension":[{"\ud800":"x"}]}""", "extension[0]")]
    [InlineData("""{"resourceType":"Patient","id":"p1","managingOrganization":{"reference":"Organization/\ud800"}}""", "managingOrganization.reference")]
    [InlineData("""{"resourceType":"Patient","id":"p1","managingOrganization":{"display":"\ud800"}}""", "managingOrganization.display")]
    [InlineData("""{"resourceType":"Patient","id":"p1","meta":{"lastUpdated":"\ud800"}}""", "meta.lastUpdated")]
    [InlineData("""{"resourceType":"Patient","id":"p1","identifier":[{"value":"\ud800"}]}""", "identifier[0].value")]
    [InlineData("""{"resourceType":"Patient","id":"p1","identifier":[{"type":{"coding":[{"code":"\ud800"}]}}]}""", "identifier[0].type.coding[0].code")]
    [InlineData("""{"resourceType":"Patient","id":"p1","identifier":[{"type":{"\ud800":"x"}}]}""", "identifier[0].type")]
    [InlineData("""{"resourceType":"Patient","id":"p1","_gender":{"extension":[{"url":"u","valueString":"\ud800"}]}}""", "_gender.extension[0].valueString")]
    [InlineData("""{"resourceType":"Patient","id":"p1","unmapped":{"\ud800":1}}""", "unmapped")]
    public async Task GivenALoneSurrogateEscape_WhenProjected_ThenRejectedAtItsPath(string json, string path)
    {
        var projection = await PatientProjectionAsync();

        var error = projection.Rejection(json);

        error.Path.ShouldBe(path);
        error.Message.ShouldBe($"The resource cannot be projected to the Silver table: {path} contains text that is not valid Unicode.");
    }

    [Theory]
    [InlineData("high surrogate")]
    [InlineData("low surrogate")]
    [InlineData("reversed pair")]
    [InlineData("high surrogate at the end")]
    [InlineData("id")]
    [InlineData("nested property name")]
    [InlineData("resource property name")]
    [InlineData("extension text")]
    [InlineData("unmapped property")]
    [InlineData("parsed resource property name")]
    public async Task GivenALoneSurrogateInAResourceBuiltInCode_WhenProjected_ThenRejectedAtItsPath(string name)
    {
        var projection = await PatientProjectionAsync();
        var (build, path) = BuiltInCode[name];

        var error = Should.Throw<FabricSilverProjectionException>(
            () => projection.Project(new ResourceJsonNode(build()), Ingestion()));

        error.Path.ShouldBe(path);
    }

    [Fact]
    public async Task GivenInvalidTextNextToAValue_WhenRejected_ThenTheMessageOmitsTheValue()
    {
        const string Secret = "invented-secret-marker";
        var projection = await PatientProjectionAsync();

        var error = projection.Rejection($$"""{"resourceType":"Patient","id":"p1","name":[{"family":"{{Secret}}\ud800"}]}""");

        error.Message.ShouldNotContain(Secret);
    }

    [Fact]
    public async Task GivenSurrogatePairs_WhenProjected_ThenTheTextIsKept()
    {
        const string Face = "\ud83d\ude00";
        var projection = await PatientProjectionAsync();

        var parsed = projection.Project("""{"resourceType":"Patient","id":"p1","gender":"\ud83d\ude00","_gender":{"\ud83d\ude00":1}}""");
        var built = projection.Project(new ResourceJsonNode(Patient(new() { ["gender"] = Face, [Face] = 1 })), Ingestion());

        Gender(parsed).GetString().ShouldBe(Face);
        Gender(built).GetString().ShouldBe(Face);
    }

    [Fact]
    public void GivenLineageThatIsNotValidUnicode_WhenIngestionCreated_ThenRejected()
    {
        Should.Throw<ArgumentException>(() => new FabricSilverIngestion($"{Namespace}/{Lone}", FilePath, IngestedAt))
            .ParamName.ShouldBe("sourceNamespace");
        Should.Throw<ArgumentException>(() => new FabricSilverIngestion(Namespace, $"{FilePath}{Lone}", IngestedAt))
            .ParamName.ShouldBe("filePath");
    }

    private static JsonObject Patient(JsonObject members)
    {
        var resource = new JsonObject { ["resourceType"] = "Patient", ["id"] = "p1" };
        foreach (var (name, value) in members.ToList())
        {
            members.Remove(name);
            resource[name] = value;
        }

        return resource;
    }

    private static System.Text.Json.JsonElement Gender(FabricSilverRow row) =>
        row.Cells[row.ColumnNames.ToList().IndexOf("gender")];

    private static Task<FabricSilverProjection> PatientProjectionAsync() => CreateAsync(
        "Patient",
        Field("gender", "string"),
        Field("birthDate", "date"),
        Field("name", ArrayOf(Strings("family"))),
        Field("extension", "string"),
        Field("managingOrganization", Reference()),
        Field("meta", Struct(Field("lastUpdated", "timestamp"))),
        Field("identifier", ArrayOf(Identifier(ShallowReference()))));
}
