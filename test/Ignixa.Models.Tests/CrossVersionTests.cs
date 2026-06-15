// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Shouldly;
using Xunit;

namespace Ignixa.Models.Tests;

/// <summary>
/// The cross-version thesis: a single parsed JSON node can be viewed through the R4 AND R5 typed
/// facades at once (zero-copy, same backing node); a method written against the shared base type
/// (<c>Ignixa.Models.Patient</c>) is Liskov-substitutable for both; INCOMPATIBLE elements really do
/// diverge per version (<c>Attachment.size</c>: <see cref="int"/> in R4, <see cref="long"/> in R5);
/// and <c>AsVersion(FhirVersion)</c> dispatches to the correct concrete subclass at runtime.
/// </summary>
public sealed class CrossVersionTests
{
    private const string PatientJson =
        """
        {
          "resourceType": "Patient",
          "id": "example",
          "gender": "female",
          "birthDate": "1974-12-25",
          "name": [ { "family": "Chalmers", "given": [ "Jean" ] } ]
        }
        """;

    [Fact]
    public void GivenSameNode_WhenViewedAsBothVersions_ThenBothReadThroughTheSameBackingNode()
    {
        var resource = ResourceJsonNode.Parse(PatientJson);

        Ignixa.Models.R4.Patient p4 = resource.As<Ignixa.Models.R4.Patient>();
        Ignixa.Models.R5.Patient p5 = resource.As<Ignixa.Models.R5.Patient>();

        p4.Gender.ShouldBe(AdministrativeGender.Female);
        p5.Gender.ShouldBe(AdministrativeGender.Female);
        p4.BirthDate.ShouldBe("1974-12-25");
        p5.BirthDate.ShouldBe("1974-12-25");

        ReferenceEquals(p4.MutableNode, p5.MutableNode).ShouldBeTrue();
    }

    [Fact]
    public void GivenMutationThroughOneVersionView_WhenReadThroughTheOther_ThenItSeesTheChange()
    {
        var resource = ResourceJsonNode.Parse(PatientJson);

        Ignixa.Models.R4.Patient p4 = resource.As<Ignixa.Models.R4.Patient>();
        Ignixa.Models.R5.Patient p5 = resource.As<Ignixa.Models.R5.Patient>();

        p4.Gender = AdministrativeGender.Male;

        p5.Gender.ShouldBe(AdministrativeGender.Male);
        resource.MutableNode["gender"]!.GetValue<string>().ShouldBe("male");
    }

    // -- Base substitutability (Liskov) -----------------------------------------------------------

    private static string? ReadBirthDate(Patient patient) => patient.BirthDate;

    [Fact]
    public void GivenMethodTypedAgainstBasePatient_WhenPassedEitherVersion_ThenItWorks()
    {
        var resource = ResourceJsonNode.Parse(PatientJson);

        Patient asR4 = resource.As<Ignixa.Models.R4.Patient>();
        Patient asR5 = resource.As<Ignixa.Models.R5.Patient>();

        ReadBirthDate(asR4).ShouldBe("1974-12-25");
        ReadBirthDate(asR5).ShouldBe("1974-12-25");

        asR4.ShouldBeAssignableTo<Patient>();
        asR5.ShouldBeAssignableTo<Patient>();
    }

    // -- INCOMPATIBLE element divergence: Attachment.size (int? R4, long? R5) ---------------------

    [Fact]
    public void GivenAttachmentSize_WhenTypedPerVersion_ThenAccessorTypesDiffer()
    {
        // Compile-time proof: these are the actual declared accessor types.
        int? r4Size = new Ignixa.Models.R4.Attachment().Size;
        long? r5Size = new Ignixa.Models.R5.Attachment().Size;

        r4Size.ShouldBeNull();
        r5Size.ShouldBeNull();
    }

    [Fact]
    public void GivenLargeSizeBeyondInt_WhenReadThroughR5_ThenLongPreservesItWhileR4WouldTruncate()
    {
        // A byte count larger than int.MaxValue (~2.1 GB). FHIR R5 widened Attachment.size to
        // integer64, so only the long? accessor can read it without overflow.
        const long largeSize = 5_000_000_000L; // ~4.66 GiB, > int.MaxValue (2_147_483_647)

        var node = new JsonObject
        {
            ["contentType"] = "application/octet-stream",
            ["size"] = largeSize,
        };

        var r5 = new Ignixa.Models.R5.Attachment(node);
        r5.Size.ShouldBe(largeSize);

        // The same node read through the R4 int? accessor cannot represent the value: reading it as
        // a 32-bit int throws (the value does not fit), proving the R4 facade would lose/refuse it.
        // The specific type is InvalidOperationException: JsonValue.GetValue<int?>() rejects a number
        // that does not fit the target (verified against the real read path, not OverflowException).
        var r4 = new Ignixa.Models.R4.Attachment(node);
        Should.Throw<InvalidOperationException>(() => _ = r4.Size);
    }

    // -- AsVersion(FhirVersion) runtime dispatch --------------------------------------------------

    [Fact]
    public void GivenNode_WhenDispatchedByFhirVersion_ThenReturnsTheCorrectConcreteSubclass()
    {
        // The version packages self-register via [ModuleInitializer], but that is lazy. Reach an R4
        // and an R5 type (or call Register()) so both factories are present before dispatch.
        Ignixa.Models.R4.R4.Register();
        Ignixa.Models.R5.R5.Register();

        var r5Node = ResourceJsonNode.Parse(PatientJson);
        ResourceJsonNode r5 = r5Node.AsVersion(FhirVersion.R5);
        r5.ShouldBeOfType<Ignixa.Models.R5.Patient>();
        r5.FhirVersion.ShouldBe(FhirVersion.R5);

        var r4Node = ResourceJsonNode.Parse(PatientJson);
        ResourceJsonNode r4 = r4Node.AsVersion(FhirVersion.R4);
        r4.ShouldBeOfType<Ignixa.Models.R4.Patient>();
        r4.FhirVersion.ShouldBe(FhirVersion.R4);
    }

    [Fact]
    public void GivenDispatchedBaseInstance_WhenReinterpretedToVersionDelta_ThenDeltaIsReachable()
    {
        Ignixa.Models.R5.R5.Register();

        var node = ResourceJsonNode.Parse(PatientJson);
        ResourceJsonNode dispatched = node.AsVersion(FhirVersion.R5);

        // Dispatch returns the instance; a further As<R5.Patient>() reaches the version view.
        Ignixa.Models.R5.Patient r5 = dispatched.As<Ignixa.Models.R5.Patient>();
        r5.BirthDate.ShouldBe("1974-12-25");
    }

    // -- AsVersion / TryAsVersion on an UNREGISTERED resource type --------------------------------

    private const string DeviceJson =
        """{ "resourceType": "Device", "id": "x" }""";

    [Fact]
    public void GivenUnregisteredResourceType_WhenAsVersion_ThenThrowsInvalidOperationException()
    {
        // Device has no generated typed facade, so it is never registered for ANY version. This is
        // deterministic regardless of module-init order. AsVersion must throw rather than hand back a
        // silently mistyped node: a wrong-typed facade is a correctness bug, not a usable fallback.
        var node = ResourceJsonNode.Parse(DeviceJson);

        Should.Throw<InvalidOperationException>(() => node.AsVersion(FhirVersion.R4));
    }

    [Fact]
    public void GivenUnregisteredResourceType_WhenTryAsVersion_ThenReturnsFalseAndLeavesNodeUntouched()
    {
        var node = ResourceJsonNode.Parse(DeviceJson);
        FhirVersion? originalVersion = node.FhirVersion;

        bool ok = node.TryAsVersion(FhirVersion.R4, out var versioned);

        ok.ShouldBeFalse();
        ReferenceEquals(versioned, node).ShouldBeTrue();
        versioned.FhirVersion.ShouldBe(originalVersion); // NOT mutated on a miss.
    }

    // -- INCOMPATIBLE divergence UNDER MUTATION: write long via R5, read int via R4 ----------------

    [Fact]
    public void GivenSizeWrittenThroughR5Long_WhenReadThroughR4Int_ThenItThrowsInvalidOperationException()
    {
        // Write a byte count > int.MaxValue through the R5 long? setter (a CLR-long-backed JsonValue),
        // then read the SAME backing node through the R4 int? accessor. This exercises divergence under
        // MUTATION (not just a parsed literal): the value cannot be represented as a 32-bit int, so the
        // read throws InvalidOperationException (verified actual type from JsonValue.GetValue<int?>()).
        const long largeSize = 5_000_000_000L; // > int.MaxValue (2_147_483_647)
        var node = new JsonObject { ["contentType"] = "application/octet-stream" };

        var r5 = new Ignixa.Models.R5.Attachment(node);
        r5.Size = largeSize;
        r5.Size.ShouldBe(largeSize);

        var r4 = new Ignixa.Models.R4.Attachment(node);
        Should.Throw<InvalidOperationException>(() => _ = r4.Size);
    }
}
