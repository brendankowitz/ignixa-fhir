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
/// M2 unit tests for <see cref="ElementMerger"/>: a differential that introduces slicing plus
/// named slice members must carry the <c>slicing</c> metadata onto the sliced element and insert
/// the slice members (and their sub-element subtrees) contiguously after the header, keyed by
/// <c>id</c> so slice content never collides with the base element it slices.
/// </summary>
public sealed class ElementMergerSlicingTests
{
    private static JsonArray Array(string json) => (JsonNode.Parse(json) as JsonArray)!;

    private static string[] Paths(JsonArray elements)
        => elements.OfType<JsonObject>().Select(e => e["path"]!.GetValue<string>()).ToArray();

    private static string?[] SliceNames(JsonArray elements)
        => elements.OfType<JsonObject>().Select(e => e["sliceName"]?.GetValue<string>()).ToArray();

    [Fact]
    public void GivenDifferentialWithNamedSlices_WhenMerged_ThenSliceMembersInsertedContiguouslyAfterHeader()
    {
        var baseElements = Array("""
        [
          {"id":"Patient","path":"Patient"},
          {"id":"Patient.extension","path":"Patient.extension","min":0,"max":"*"},
          {"id":"Patient.name","path":"Patient.name","min":0,"max":"1"}
        ]
        """);
        var differential = Array("""
        [
          {"id":"Patient.extension","path":"Patient.extension","slicing":{"discriminator":[{"type":"value","path":"url"}],"rules":"closed"}},
          {"id":"Patient.extension:race","path":"Patient.extension","sliceName":"race","min":0,"max":"1"},
          {"id":"Patient.extension:birthsex","path":"Patient.extension","sliceName":"birthsex","min":0,"max":"1"}
        ]
        """);

        var merged = ElementMerger.Merge(baseElements, differential);

        // Slice members are inserted right after the sliced header and before the next base element.
        Paths(merged).ShouldBe(["Patient", "Patient.extension", "Patient.extension", "Patient.extension", "Patient.name"]);
        SliceNames(merged).ShouldBe([null, null, "race", "birthsex", null]);
    }

    [Fact]
    public void GivenDifferentialAddsSlicing_WhenMerged_ThenSlicingMetadataCarriedOntoBaseHeader()
    {
        var baseElements = Array("""[{"id":"Patient.extension","path":"Patient.extension","min":0,"max":"*"}]""");
        var differential = Array("""
        [{"id":"Patient.extension","path":"Patient.extension","slicing":{"discriminator":[{"type":"value","path":"url"}],"rules":"closed"}}]
        """);

        var merged = ElementMerger.Merge(baseElements, differential);

        var header = merged.OfType<JsonObject>().Single();
        header["slicing"].ShouldNotBeNull();
        header["slicing"]!["rules"]!.GetValue<string>().ShouldBe("closed");
    }

    [Fact]
    public void GivenBaseHeaderWithoutId_WhenDifferentialAddsSlicingAndSlices_ThenHeaderPreservedWithSlicing()
    {
        // Reproduces the projected-core-base case: base elements carry no `id` (only `path`), while
        // the differential header carries an `id`. The header must still merge onto the base element
        // and survive alongside the inserted slice members.
        var baseElements = Array("""
        [
          {"path":"Patient"},
          {"path":"Patient.extension","min":0,"max":"*"},
          {"path":"Patient.name","min":0,"max":"1"}
        ]
        """);
        var differential = Array("""
        [
          {"id":"Patient.extension","path":"Patient.extension","slicing":{"discriminator":[{"type":"value","path":"url"}],"rules":"open"}},
          {"id":"Patient.extension:race","path":"Patient.extension","sliceName":"race","min":0,"max":"1"}
        ]
        """);

        var merged = ElementMerger.Merge(baseElements, differential);

        var header = merged.OfType<JsonObject>()
            .SingleOrDefault(e => e["path"]!.GetValue<string>() == "Patient.extension" && e["sliceName"] is null);
        header.ShouldNotBeNull("the base header must survive the merge");
        header!["slicing"].ShouldNotBeNull();
        SliceNames(merged).ShouldBe([null, null, "race", null]);
    }

    [Fact]
    public void GivenDifferentialWithSlicesButNoSlicingHeader_WhenMerged_ThenExtensionSlicingIsSynthesizedOnBaseHeader()
    {
        // The US Core us-core-patient shape: the differential lists extension slice members but omits
        // the slicing header (slicing lives only in the IG's shipped snapshot). Snapshot generation
        // must synthesize the default extension slicing (value:url, open) so the sliced element still
        // carries discriminators.
        var baseElements = Array("""
        [
          {"path":"Patient"},
          {"path":"Patient.extension","min":0,"max":"*"},
          {"path":"Patient.name","min":0,"max":"1"}
        ]
        """);
        var differential = Array("""
        [
          {"id":"Patient.extension:race","path":"Patient.extension","sliceName":"race","min":0,"max":"1","type":[{"code":"Extension","profile":["http://x/race"]}]}
        ]
        """);

        var merged = ElementMerger.Merge(baseElements, differential);

        var header = merged.OfType<JsonObject>()
            .SingleOrDefault(e => e["path"]!.GetValue<string>() == "Patient.extension" && e["sliceName"] is null);
        header.ShouldNotBeNull("the base extension header must survive and carry synthesized slicing");
        var slicing = header!["slicing"];
        slicing.ShouldNotBeNull();
        slicing!["discriminator"]![0]!["path"]!.GetValue<string>().ShouldBe("url");
        SliceNames(merged).ShouldBe([null, null, "race", null]);
    }

    [Fact]
    public void GivenSliceWithSubElements_WhenMerged_ThenSubElementsDoNotOverwriteBaseChildren()
    {
        var baseElements = Array("""
        [
          {"id":"Patient.identifier","path":"Patient.identifier","min":0,"max":"*"},
          {"id":"Patient.identifier.system","path":"Patient.identifier.system","min":0,"max":"1"}
        ]
        """);
        var differential = Array("""
        [
          {"id":"Patient.identifier","path":"Patient.identifier","slicing":{"discriminator":[{"type":"value","path":"system"}],"rules":"open"}},
          {"id":"Patient.identifier:mrn","path":"Patient.identifier","sliceName":"mrn","min":1,"max":"1"},
          {"id":"Patient.identifier:mrn.system","path":"Patient.identifier.system","min":1,"max":"1","fixedUri":"http://hospital.example.org/mrn"}
        ]
        """);

        var merged = ElementMerger.Merge(baseElements, differential);

        // The base Patient.identifier.system must retain its own (min 0) cardinality — the slice's
        // constrained system (min 1, fixedUri) is a separate id and must not overwrite the base.
        var baseSystem = merged.OfType<JsonObject>()
            .Single(e => e["id"]!.GetValue<string>() == "Patient.identifier.system");
        baseSystem["min"]!.GetValue<int>().ShouldBe(0);
        baseSystem["fixedUri"].ShouldBeNull();

        var sliceSystem = merged.OfType<JsonObject>()
            .Single(e => e["id"]!.GetValue<string>() == "Patient.identifier:mrn.system");
        sliceSystem["fixedUri"]!.GetValue<string>().ShouldBe("http://hospital.example.org/mrn");
    }
}
