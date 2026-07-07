// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.PackageManagement.Infrastructure.Snapshot;
using Shouldly;
using Xunit;

namespace Ignixa.PackageManagement.Tests.Snapshot;

/// <summary>
/// Unit tests for <see cref="SnapshotGenerator"/> orchestration: existing-snapshot pass-through,
/// single- and multi-level base recursion, differential-as-snapshot for a root with no base,
/// unresolvable base, and circular base-chain detection.
/// </summary>
public sealed class SnapshotGeneratorTests
{
    private static JsonObject Sd(string json) => (JsonNode.Parse(json) as JsonObject)!;

    private static string PathAt(JsonArray elements, string path)
        => elements.OfType<JsonObject>().Single(e => e["path"]?.GetValue<string>() == path)["min"]!.GetValue<int>().ToString();

    [Fact]
    public void GivenStructureDefinitionWithSnapshot_WhenGenerated_ThenExistingSnapshotUsedAsIs()
    {
        var sd = Sd("""
        {
          "resourceType":"StructureDefinition","url":"http://x/A","baseDefinition":"http://x/Base",
          "differential":{"element":[{"path":"A.name","min":1}]},
          "snapshot":{"element":[{"path":"A"},{"path":"A.name","min":0,"max":"1"}]}
        }
        """);
        var resolver = new FixtureBaseResolver([]);

        var generated = new SnapshotGenerator().GenerateSnapshotElements(sd, resolver);

        generated.ShouldNotBeNull();
        PathAt(generated!, "A.name").ShouldBe("0", "existing snapshot must not be re-merged with the differential");
    }

    [Fact]
    public void GivenDifferentialAndBaseSnapshot_WhenGenerated_ThenBaseIsMergedWithDifferential()
    {
        var baseSd = Sd("""
        {"resourceType":"StructureDefinition","url":"http://x/Base",
         "snapshot":{"element":[{"path":"A"},{"path":"A.name","min":0,"max":"*"}]}}
        """);
        var profile = Sd("""
        {"resourceType":"StructureDefinition","url":"http://x/A","baseDefinition":"http://x/Base",
         "differential":{"element":[{"path":"A.name","min":1}]}}
        """);
        var resolver = new FixtureBaseResolver([baseSd]);

        var generated = new SnapshotGenerator().GenerateSnapshotElements(profile, resolver);

        generated.ShouldNotBeNull();
        generated!.OfType<JsonObject>().Select(e => e["path"]!.GetValue<string>()).ShouldBe(["A", "A.name"]);
        PathAt(generated, "A.name").ShouldBe("1");
    }

    [Fact]
    public void GivenProfileOnProfileChain_WhenGenerated_ThenTighteningsFromBothLevelsApply()
    {
        var core = Sd("""
        {"resourceType":"StructureDefinition","url":"http://x/Core",
         "snapshot":{"element":[{"path":"A"},{"path":"A.name","min":0,"max":"*"},{"path":"A.gender","min":0,"max":"1"}]}}
        """);
        var mid = Sd("""
        {"resourceType":"StructureDefinition","url":"http://x/Mid","baseDefinition":"http://x/Core",
         "differential":{"element":[{"path":"A.name","min":1}]}}
        """);
        var leaf = Sd("""
        {"resourceType":"StructureDefinition","url":"http://x/Leaf","baseDefinition":"http://x/Mid",
         "differential":{"element":[{"path":"A.gender","min":1}]}}
        """);
        var resolver = new FixtureBaseResolver([core, mid]);

        var generated = new SnapshotGenerator().GenerateSnapshotElements(leaf, resolver);

        generated.ShouldNotBeNull();
        PathAt(generated!, "A.name").ShouldBe("1", "tightening from the intermediate profile must survive");
        PathAt(generated, "A.gender").ShouldBe("1", "tightening from the leaf profile must apply");
    }

    [Fact]
    public void GivenNoBaseDefinition_WhenGenerated_ThenDifferentialIsUsedAsSnapshot()
    {
        var sd = Sd("""
        {"resourceType":"StructureDefinition","url":"http://x/Root",
         "differential":{"element":[{"path":"Root"},{"path":"Root.value","min":1,"max":"1"}]}}
        """);
        var resolver = new FixtureBaseResolver([]);

        var generated = new SnapshotGenerator().GenerateSnapshotElements(sd, resolver);

        generated.ShouldNotBeNull();
        generated!.OfType<JsonObject>().Select(e => e["path"]!.GetValue<string>()).ShouldBe(["Root", "Root.value"]);
    }

    [Fact]
    public void GivenUnresolvableBase_WhenGenerated_ThenReturnsNull()
    {
        var sd = Sd("""
        {"resourceType":"StructureDefinition","url":"http://x/A","baseDefinition":"http://x/Missing",
         "differential":{"element":[{"path":"A.name","min":1}]}}
        """);
        var resolver = new FixtureBaseResolver([]);

        var generated = new SnapshotGenerator().GenerateSnapshotElements(sd, resolver);

        generated.ShouldBeNull();
    }

    [Fact]
    public void GivenCircularBaseChain_WhenGenerated_ThenThrowsSnapshotGenerationException()
    {
        var a = Sd("""
        {"resourceType":"StructureDefinition","url":"http://x/A","baseDefinition":"http://x/B",
         "differential":{"element":[{"path":"A.name","min":1}]}}
        """);
        var b = Sd("""
        {"resourceType":"StructureDefinition","url":"http://x/B","baseDefinition":"http://x/A",
         "differential":{"element":[{"path":"A.gender","min":1}]}}
        """);
        var resolver = new FixtureBaseResolver([a, b]);

        Should.Throw<SnapshotGenerationException>(
            () => new SnapshotGenerator().GenerateSnapshotElements(a, resolver));
    }
}
