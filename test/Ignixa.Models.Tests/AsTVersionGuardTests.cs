// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;
using Shouldly;
using Xunit;

namespace Ignixa.Models.Tests;

/// <summary>
/// <see cref="ResourceJsonNode.As{T}"/>'s version guard: every generated facade carries a
/// <see cref="Ignixa.Serialization.CompatibleFhirVersionsAttribute"/>, and As&lt;T&gt;() throws when a
/// version-tagged node is reinterpreted through a facade that doesn't list that version -- e.g. R4 data
/// read through an R5-only accessor, which would otherwise be a silent misread (same property names,
/// different meanings) rather than a compile or runtime error.
/// </summary>
public sealed class AsTVersionGuardTests
{
    private const string PatientJson = """{ "resourceType": "Patient", "id": "example" }""";

    [Fact]
    public void GivenR4TaggedNode_WhenAsR5OnlyType_ThenThrowsInvalidCastException()
    {
        var r4Patient = ResourceJsonNode.Parse(PatientJson).As<Ignixa.Models.R4.Patient>();
        r4Patient.FhirVersion.ShouldBe(FhirVersion.R4);

        var ex = Should.Throw<InvalidCastException>(() => r4Patient.As<Ignixa.Models.R5.Patient>());
        ex.Message.ShouldContain("R4");
        ex.Message.ShouldContain("R5");
    }

    [Fact]
    public void GivenR5TaggedNode_WhenAsR4OnlyType_ThenThrowsInvalidCastException()
    {
        var r5Patient = ResourceJsonNode.Parse(PatientJson).As<Ignixa.Models.R5.Patient>();
        r5Patient.FhirVersion.ShouldBe(FhirVersion.R5);

        Should.Throw<InvalidCastException>(() => r5Patient.As<Ignixa.Models.R4.Patient>());
    }

    [Fact]
    public void GivenR4TaggedNode_WhenAsSharedBaseType_ThenSucceeds()
    {
        // The shared base's CompatibleFhirVersions lists every version it's classified into (R4, R5),
        // so a tagged node reinterpreted as the base -- not a specific version's subclass -- is fine.
        var r4Patient = ResourceJsonNode.Parse(PatientJson).As<Ignixa.Models.R4.Patient>();

        Patient asBase = r4Patient.As<Patient>();

        asBase.ShouldNotBeNull();
        asBase.FhirVersion.ShouldBe(FhirVersion.R4);
    }

    [Fact]
    public void GivenUntaggedNode_WhenAsSingleVersionType_ThenSucceedsAndStampsThatVersion()
    {
        // No FhirVersion set anywhere -- the guard must stay permissive for untagged callers (today's
        // behavior), and the bonus stamping behavior tags the result since R5.Patient is unambiguous.
        var untagged = ResourceJsonNode.Parse(PatientJson);
        untagged.FhirVersion.ShouldBeNull();

        var r5Patient = untagged.As<Ignixa.Models.R5.Patient>();

        r5Patient.FhirVersion.ShouldBe(FhirVersion.R5);
    }

    [Fact]
    public void GivenUnspecifiedTaggedNode_WhenAsEitherVersionType_ThenDoesNotThrow()
    {
        // FhirVersion.Unspecified means "assume latest for comparisons", not "unknown" -- but it is not
        // a hard constraint either, so it is exempt from the guard the same as an untagged node.
        var node = ResourceJsonNode.Parse(PatientJson);
        node.FhirVersion = FhirVersion.Unspecified;

        Should.NotThrow(() => node.As<Ignixa.Models.R4.Patient>());
        Should.NotThrow(() => node.As<Ignixa.Models.R5.Patient>());
    }

    [Fact]
    public void GivenMismatchedVersions_WhenValidateFalse_ThenBypassesTheGuard()
    {
        // The existing escape hatch covers the version check too, not just the resource-type check.
        var r4Patient = ResourceJsonNode.Parse(PatientJson).As<Ignixa.Models.R4.Patient>();

        Should.NotThrow(() => r4Patient.As<Ignixa.Models.R5.Patient>(validate: false));
    }
}
