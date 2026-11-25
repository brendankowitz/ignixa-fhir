// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Nodes;
using Ignixa.Serialization.SourceNodes;
using static Ignixa.SqlOnFhir.Fabric.Tests.SilverProjectionHarness;
using static Ignixa.SqlOnFhir.Fabric.Tests.SyntheticSilverRoot;

namespace Ignixa.SqlOnFhir.Fabric.Tests;

/// <summary>
/// A parsed object that repeats a property name, and nesting deeper than Ignixa's resource parser accepts, are rejected
/// at their path instead of escaping as an error that quotes the name or as a stack overflow that ends the process.
/// </summary>
public class FabricSilverProjectionStructureTests
{
    private const string Prefix = "The resource cannot be projected to the Silver table:";

    // Ignixa's parser cannot read a repeated name at the resource level, so every case is parsed by System.Text.Json.
    [Theory]
    [InlineData("""{"resourceType":"Patient","id":"p1","invented-secret":1,"invented-secret":2}""", "Patient")]
    [InlineData("""{"resourceType":"Patient","id":"p1","name":[{"family":"a","family":"b"}]}""", "name[0]")]
    [InlineData("""{"resourceType":"Patient","id":"p1","unmapped":{"invented-secret":1,"invented-secret":2}}""", "<property 2>")]
    public async Task GivenAnObjectThatRepeatsAPropertyName_WhenProjected_ThenRejectedAtTheObjectWithoutTheName(string json, string path)
    {
        var projection = await PatientProjectionAsync();

        var error = Should.Throw<FabricSilverProjectionException>(
            () => projection.Project(new ResourceJsonNode(JsonNode.Parse(json)!.AsObject()), Ingestion()));

        error.Path.ShouldBe(path);
        error.Message.ShouldBe($"{Prefix} {path} holds a property name more than once.");
        error.InnerException.ShouldBeNull();
    }

    // With the resource as the first level, 63 nested arrays reach the parser's limit of 64.
    [Fact]
    public async Task GivenNestingAtTheParserLimit_WhenProjected_ThenItIsStored()
    {
        var projection = await PatientProjectionAsync();
        var nested = new string('[', 63) + new string(']', 63);

        var row = projection.Project($$"""{"resourceType":"Patient","id":"p1","extension":{{nested}}}""");

        row.Cells[row.ColumnNames.ToList().IndexOf("extension")].GetString().ShouldBe(nested);
    }

    // A tree this deep can only be built in code or parsed with a raised limit; the deepest case used to overflow the stack.
    [Theory]
    [InlineData(JsonValueKind.Array, 64)]
    [InlineData(JsonValueKind.Object, 64)]
    [InlineData(JsonValueKind.Array, 100_000)]
    [InlineData(JsonValueKind.Object, 100_000)]
    public async Task GivenNestingPastTheParserLimit_WhenProjected_ThenRejectedAtTheFirstLevelTooDeep(JsonValueKind kind, int levels)
    {
        var projection = await PatientProjectionAsync();
        var resource = new JsonObject { ["resourceType"] = "Patient", ["id"] = "p1", ["extension"] = Nested(kind, levels) };

        var error = Should.Throw<FabricSilverProjectionException>(() => projection.Project(new ResourceJsonNode(resource), Ingestion()));

        var path = "extension" + string.Concat(Enumerable.Repeat(kind == JsonValueKind.Array ? "[0]" : ".<property 0>", 63));
        error.Path.ShouldBe(path);
        error.Message.ShouldBe($"{Prefix} {path} is nested more than 64 objects and arrays deep.");
    }

    private static JsonNode Nested(JsonValueKind kind, int levels)
    {
        JsonNode node = kind == JsonValueKind.Array ? new JsonArray() : new JsonObject();
        for (var level = 1; level < levels; level++)
        {
            node = kind == JsonValueKind.Array ? new JsonArray(node) : new JsonObject { ["unmapped"] = node };
        }

        return node;
    }

    private static Task<FabricSilverProjection> PatientProjectionAsync() => CreateAsync(
        "Patient",
        Field("gender", "string"),
        Field("name", ArrayOf(Strings("family"))),
        Field("extension", "string"));
}
