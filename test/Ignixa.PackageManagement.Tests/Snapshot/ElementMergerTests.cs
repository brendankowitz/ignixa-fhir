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
/// Per-facet unit tests for <see cref="ElementMerger"/>: each constrainable facet
/// (cardinality, type, fixed/pattern, binding, constraint) plus base preservation and
/// new-element insertion.
/// </summary>
public sealed class ElementMergerTests
{
    private static JsonArray Array(string json) => (JsonNode.Parse(json) as JsonArray)!;

    private static JsonObject ElementAt(JsonArray elements, string path)
        => elements.OfType<JsonObject>().Single(e => e["path"]?.GetValue<string>() == path);

    [Fact]
    public void GivenBaseMinZero_WhenDifferentialTightensToOne_ThenMergedMinIsOne()
    {
        var baseElements = Array("""[{"path":"Patient.name","min":0,"max":"*"}]""");
        var differential = Array("""[{"path":"Patient.name","min":1}]""");

        var merged = ElementMerger.Merge(baseElements, differential);

        ElementAt(merged, "Patient.name")["min"]!.GetValue<int>().ShouldBe(1);
        ElementAt(merged, "Patient.name")["max"]!.GetValue<string>().ShouldBe("*");
    }

    [Fact]
    public void GivenBaseMaxUnbounded_WhenDifferentialRestrictsToOne_ThenMergedMaxIsOne()
    {
        var baseElements = Array("""[{"path":"Patient.name","min":0,"max":"*"}]""");
        var differential = Array("""[{"path":"Patient.name","max":"1"}]""");

        var merged = ElementMerger.Merge(baseElements, differential);

        ElementAt(merged, "Patient.name")["max"]!.GetValue<string>().ShouldBe("1");
    }

    [Fact]
    public void GivenBaseChoiceOfTypes_WhenDifferentialRestrictsToOneType_ThenMergedTypeIsRestricted()
    {
        var baseElements = Array("""[{"path":"Observation.value[x]","type":[{"code":"Quantity"},{"code":"CodeableConcept"},{"code":"string"}]}]""");
        var differential = Array("""[{"path":"Observation.value[x]","type":[{"code":"Quantity"}]}]""");

        var merged = ElementMerger.Merge(baseElements, differential);

        var types = ElementAt(merged, "Observation.value[x]")["type"] as JsonArray;
        types!.Count.ShouldBe(1);
        types[0]!["code"]!.GetValue<string>().ShouldBe("Quantity");
    }

    [Fact]
    public void GivenNoBaseFixedValue_WhenDifferentialInjectsFixed_ThenMergedCarriesFixed()
    {
        var baseElements = Array("""[{"path":"Observation.status","min":1,"max":"1"}]""");
        var differential = Array("""[{"path":"Observation.status","fixedCode":"final"}]""");

        var merged = ElementMerger.Merge(baseElements, differential);

        ElementAt(merged, "Observation.status")["fixedCode"]!.GetValue<string>().ShouldBe("final");
    }

    [Fact]
    public void GivenNoBasePattern_WhenDifferentialInjectsPattern_ThenMergedCarriesPattern()
    {
        var baseElements = Array("""[{"path":"Observation.code","min":1,"max":"1"}]""");
        var differential = Array("""[{"path":"Observation.code","patternCodeableConcept":{"coding":[{"system":"http://loinc.org","code":"1234-5"}]}}]""");

        var merged = ElementMerger.Merge(baseElements, differential);

        var pattern = ElementAt(merged, "Observation.code")["patternCodeableConcept"] as JsonObject;
        pattern.ShouldNotBeNull();
        pattern!["coding"]![0]!["code"]!.GetValue<string>().ShouldBe("1234-5");
    }

    [Fact]
    public void GivenBaseExampleBinding_WhenDifferentialOverridesToRequired_ThenMergedBindingIsRequired()
    {
        var baseElements = Array("""[{"path":"Patient.gender","binding":{"strength":"example","valueSet":"http://example.org/vs"}}]""");
        var differential = Array("""[{"path":"Patient.gender","binding":{"strength":"required","valueSet":"http://hl7.org/fhir/ValueSet/administrative-gender"}}]""");

        var merged = ElementMerger.Merge(baseElements, differential);

        var binding = ElementAt(merged, "Patient.gender")["binding"] as JsonObject;
        binding!["strength"]!.GetValue<string>().ShouldBe("required");
        binding["valueSet"]!.GetValue<string>().ShouldBe("http://hl7.org/fhir/ValueSet/administrative-gender");
    }

    [Fact]
    public void GivenBaseConstraint_WhenDifferentialAddsConstraint_ThenMergedUnionsBoth()
    {
        var baseElements = Array("""[{"path":"Patient","constraint":[{"key":"dom-2","expression":"a"}]}]""");
        var differential = Array("""[{"path":"Patient","constraint":[{"key":"prof-1","expression":"b"}]}]""");

        var merged = ElementMerger.Merge(baseElements, differential);

        var constraints = ElementAt(merged, "Patient")["constraint"] as JsonArray;
        constraints!.Select(c => c!["key"]!.GetValue<string>()).ShouldBe(["dom-2", "prof-1"]);
    }

    [Fact]
    public void GivenBaseElementUntouchedByDifferential_WhenMerged_ThenPreservedVerbatim()
    {
        var baseElements = Array("""[{"path":"Patient.name","min":0,"max":"*"},{"path":"Patient.birthDate","min":0,"max":"1"}]""");
        var differential = Array("""[{"path":"Patient.name","min":1}]""");

        var merged = ElementMerger.Merge(baseElements, differential);

        var birthDate = ElementAt(merged, "Patient.birthDate");
        birthDate["min"]!.GetValue<int>().ShouldBe(0);
        birthDate["max"]!.GetValue<string>().ShouldBe("1");
    }

    [Fact]
    public void GivenBasePreservesOrder_WhenDifferentialTightensMiddleElement_ThenOrderUnchanged()
    {
        var baseElements = Array("""[{"path":"Patient"},{"path":"Patient.name","min":0},{"path":"Patient.gender","min":0}]""");
        var differential = Array("""[{"path":"Patient.name","min":1}]""");

        var merged = ElementMerger.Merge(baseElements, differential);

        merged.OfType<JsonObject>().Select(e => e["path"]!.GetValue<string>())
            .ShouldBe(["Patient", "Patient.name", "Patient.gender"]);
    }

    [Fact]
    public void GivenDifferentialIntroducesNewChild_WhenMerged_ThenInsertedAfterParentSubtree()
    {
        var baseElements = Array("""[{"path":"Patient"},{"path":"Patient.name","min":0},{"path":"Patient.gender","min":0}]""");
        var differential = Array("""[{"path":"Patient.name.use","min":1,"max":"1"}]""");

        var merged = ElementMerger.Merge(baseElements, differential);

        merged.OfType<JsonObject>().Select(e => e["path"]!.GetValue<string>())
            .ShouldBe(["Patient", "Patient.name", "Patient.name.use", "Patient.gender"]);
    }
}
