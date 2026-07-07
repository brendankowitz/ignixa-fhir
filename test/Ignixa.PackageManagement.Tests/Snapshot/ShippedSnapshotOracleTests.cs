// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.PackageManagement.Infrastructure.Snapshot;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Ignixa.PackageManagement.Tests.Snapshot;

/// <summary>
/// Correctness gate for the snapshot generator: for R4 core StructureDefinitions that ship BOTH a
/// differential and a snapshot, strip the snapshot, regenerate it from the differential + base,
/// and assert the generated element list matches the shipped snapshot on element paths,
/// cardinalities, types, and bindings. Any divergence is a merger bug, not a test-data quirk.
/// </summary>
/// <remarks>
/// The fixtures are constraint-derivation profiles (base and derived share a root type) without
/// slicing — the M1 target. Specialization (root rebasing) and slicing are M2/M3 and are excluded
/// here by construction. Sourced from <c>hl7.fhir.r4.core#4.0.1</c>.
/// </remarks>
public sealed class ShippedSnapshotOracleTests(ITestOutputHelper output)
{
    private static readonly string SnapshotDataDir = Path.Combine("TestData", "Snapshot");

    private readonly ITestOutputHelper _output = output;

    public static TheoryData<string> ProfileFixtures() => new()
    {
        "StructureDefinition-shareablemeasure.json",
        "StructureDefinition-cqllibrary.json",
        "StructureDefinition-shareablelibrary.json",
        "StructureDefinition-actualgroup.json",
        "StructureDefinition-groupdefinition.json",
        "StructureDefinition-ehrsrle-provenance.json",
        "StructureDefinition-synthesis.json",
    };

    [Theory]
    [MemberData(nameof(ProfileFixtures))]
    public void GivenDualFormProfile_WhenSnapshotGeneratedFromDifferential_ThenFacetsMatchShippedSnapshot(
        string profileFixture)
    {
        var profile = LoadProfile(profileFixture);
        var shipped = ElementsOf(profile.ShippedSnapshot);

        var generated = new SnapshotGenerator()
            .GenerateSnapshotElements(profile.DifferentialOnly, LoadBaseResolver());

        generated.ShouldNotBeNull();

        var generatedByKey = IndexByKey(generated!);
        var shippedByKey = IndexByKey(shipped);

        var missing = shippedByKey.Keys.Except(generatedByKey.Keys).ToList();
        var extra = generatedByKey.Keys.Except(shippedByKey.Keys).ToList();
        missing.ShouldBeEmpty($"generated snapshot is missing element(s): {Format(missing)}");
        extra.ShouldBeEmpty($"generated snapshot has unexpected element(s): {Format(extra)}");

        var facetMismatches = new List<string>();
        foreach (var (key, shippedElement) in shippedByKey)
        {
            if (SnapshotFacet.HasSlicing(shippedElement))
            {
                continue;
            }

            var generatedFacet = SnapshotFacet.Describe(generatedByKey[key]);
            var shippedFacet = SnapshotFacet.Describe(shippedElement);
            if (generatedFacet != shippedFacet)
            {
                facetMismatches.Add($"{key.Path}{(key.Slice is null ? string.Empty : ":" + key.Slice)}\n  gen={generatedFacet}\n  ship={shippedFacet}");
            }
        }

        _output.WriteLine($"{profileFixture}: {shippedByKey.Count} elements, {facetMismatches.Count} facet mismatch(es)");
        facetMismatches.ShouldBeEmpty(string.Join("\n", facetMismatches));
    }

    private static string Format(IEnumerable<(string Path, string? Slice)> keys)
        => string.Join(", ", keys.Select(k => k.Path + (k.Slice is null ? string.Empty : ":" + k.Slice)));

    private static Dictionary<(string Path, string? Slice), JsonObject> IndexByKey(JsonArray elements)
    {
        var map = new Dictionary<(string, string?), JsonObject>();
        foreach (var node in elements)
        {
            if (node is JsonObject element)
            {
                map[SnapshotFacet.KeyOf(element)] = element;
            }
        }

        return map;
    }

    private static JsonArray ElementsOf(JsonObject snapshotOrDifferential)
        => (snapshotOrDifferential["element"] as JsonArray)!;

    private static ProfileFixture LoadProfile(string fixture)
    {
        var path = Path.Combine(SnapshotDataDir, "Profiles", fixture);
        var sd = (JsonNode.Parse(File.ReadAllText(path)) as JsonObject)!;
        var shipped = (sd["snapshot"] as JsonObject)!;

        var differentialOnly = sd.DeepClone().AsObject();
        differentialOnly.Remove("snapshot");

        return new ProfileFixture(differentialOnly, shipped);
    }

    private static FixtureBaseResolver LoadBaseResolver()
    {
        var baseDir = Path.Combine(SnapshotDataDir, "Bases");
        var bases = Directory.EnumerateFiles(baseDir, "*.json")
            .Select(f => (JsonNode.Parse(File.ReadAllText(f)) as JsonObject)!);
        return new FixtureBaseResolver(bases);
    }

    private sealed record ProfileFixture(JsonObject DifferentialOnly, JsonObject ShippedSnapshot);
}
