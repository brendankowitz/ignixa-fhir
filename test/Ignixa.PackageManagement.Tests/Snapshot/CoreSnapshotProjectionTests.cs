// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.PackageManagement.Infrastructure;
using Ignixa.PackageManagement.Models;
using Ignixa.Specification.Generated;
using Shouldly;
using Xunit;

namespace Ignixa.PackageManagement.Tests.Snapshot;

/// <summary>
/// The core snapshot a differential-only profile is merged onto, as projected from the generated core metadata. Regressions
/// fail at 635c8f6, where a core choice element named by its base name ("value") was projected without its "[x]", so a
/// differential constraining "value[x]" added a second element instead of constraining the first, and a core Reference kept
/// only its first target profile.
/// </summary>
public sealed class CoreSnapshotProjectionTests
{
    // Regression: at 635c8f6 the adapted extension had both "value" (every type) and "value[x]" (code), max "*".
    [Fact]
    public void GivenADifferentialOnlyExtensionConstrainingValueX_WhenLayeredOverCore_ThenOneChoiceElementCarriesTheConstraint()
    {
        var provider = Layer("ext-code", "http://example.org/fhir/StructureDefinition/ext-code", """
            {"resourceType":"StructureDefinition","id":"ext-code","url":"http://example.org/fhir/StructureDefinition/ext-code","name":"ExtCode","status":"active","fhirVersion":"4.0.1",
             "kind":"complex-type","abstract":false,"type":"Extension","baseDefinition":"http://hl7.org/fhir/StructureDefinition/Extension","derivation":"constraint",
             "differential":{"element":[{"id":"Extension","path":"Extension"},{"id":"Extension.value[x]","path":"Extension.value[x]","min":1,"type":[{"code":"code"}]}]}}
            """);

        var extension = provider.GetTypeDefinition("http://example.org/fhir/StructureDefinition/ext-code");

        extension.ShouldNotBeNull();
        var value = extension.Children.Where(child => child.Info.Name.StartsWith("value", StringComparison.Ordinal)).ShouldHaveSingleItem();
        value.Info.Name.ShouldBe("value[x]");
        value.Info.IsChoiceElement.ShouldBeTrue();
        var constrained = (ITypeExtended)value;
        constrained.Types.Select(type => type.Code).ShouldBe(["code"]);
        constrained.Min.ShouldBe(1);
        constrained.Max.ShouldBe("1");
    }

    // Regression: at 635c8f6 an Observation profile not touching subject kept Reference(Patient) only.
    [Fact]
    public void GivenADifferentialOnlyProfileNotConstrainingAReference_WhenLayeredOverCore_ThenItKeepsEveryCoreTarget()
    {
        var provider = Layer("obs-min", "http://example.org/fhir/StructureDefinition/obs-min", """
            {"resourceType":"StructureDefinition","id":"obs-min","url":"http://example.org/fhir/StructureDefinition/obs-min","name":"ObsMin","status":"active","fhirVersion":"4.0.1",
             "kind":"resource","abstract":false,"type":"Observation","baseDefinition":"http://hl7.org/fhir/StructureDefinition/Observation","derivation":"constraint",
             "differential":{"element":[{"id":"Observation.code","path":"Observation.code","short":"x"}]}}
            """);

        var subject = (ITypeExtended)provider.GetTypeDefinition("http://example.org/fhir/StructureDefinition/obs-min")!.Children.Single(child => child.Info.Name == "subject");

        subject.Types.Select(type => type.TargetProfile).ShouldBe(
        [
            "http://hl7.org/fhir/StructureDefinition/Patient",
            "http://hl7.org/fhir/StructureDefinition/Group",
            "http://hl7.org/fhir/StructureDefinition/Device",
            "http://hl7.org/fhir/StructureDefinition/Location",
        ], ignoreOrder: true);
    }

    private static ProfileLayeredSchemaProvider Layer(string id, string canonical, string json) =>
        new(new R4CoreSchemaProvider(), [new ExtractedResource { ResourceType = "StructureDefinition", Canonical = canonical, ResourceId = id, ResourceJson = json, FhirVersion = "4.0.1" }]);
}
