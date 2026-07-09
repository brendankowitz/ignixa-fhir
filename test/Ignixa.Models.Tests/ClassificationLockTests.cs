// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Reflection;
using Shouldly;
using Xunit;

namespace Ignixa.Models.Tests;

/// <summary>
/// Structural lock on the type-classifier's outcomes. These assert over the BUILT assemblies via
/// reflection (no FHIR-package load), so they pin the classification contract cheaply: an IDENTICAL
/// type lives once in the base with no per-version subclass; a SUBCLASSED type's base is the shared
/// base type; an INCOMPATIBLE element is typed differently per version.
/// </summary>
public sealed class ClassificationLockTests
{
    private static readonly Assembly SerializationAssembly = typeof(Coding).Assembly;
    private static readonly Assembly R4Assembly = typeof(Ignixa.Models.R4.Patient).Assembly;
    private static readonly Assembly R5Assembly = typeof(Ignixa.Models.R5.Patient).Assembly;

    [Fact]
    public void GivenIdenticalType_WhenClassified_ThenItLivesOnlyInTheSharedBaseWithNoSubclass()
    {
        // Coding is byte-identical across R4/R5 -> base-only, no per-version subclass.
        typeof(Coding).Namespace.ShouldBe("Ignixa.Models");

        R4Assembly.GetType("Ignixa.Models.R4.Coding").ShouldBeNull();
        R5Assembly.GetType("Ignixa.Models.R5.Coding").ShouldBeNull();
    }

    [Fact]
    public void GivenIdenticalDatatype_WhenClassified_ThenMoneyIsBaseOnly()
    {
        // Money has no coded binding, so unlike Quantity (see the value-set-divergence test below) it
        // has nothing that can diverge between versions and stays genuinely Identical/base-only.
        typeof(Money).Namespace.ShouldBe("Ignixa.Models");

        R4Assembly.GetType("Ignixa.Models.R4.Money").ShouldBeNull();
        R5Assembly.GetType("Ignixa.Models.R5.Money").ShouldBeNull();
    }

    [Fact]
    public void GivenSubclassedResource_WhenClassified_ThenVersionPatientInheritsTheSharedBase()
    {
        typeof(Ignixa.Models.R4.Patient).BaseType.ShouldBe(typeof(Patient));
        typeof(Ignixa.Models.R5.Patient).BaseType.ShouldBe(typeof(Patient));

        // The shared base lives in the Serialization assembly under Ignixa.Models.
        typeof(Patient).Assembly.ShouldBe(SerializationAssembly);
        typeof(Patient).Namespace.ShouldBe("Ignixa.Models");
    }

    [Fact]
    public void GivenSubclassedDatatype_WhenClassified_ThenAttachmentInheritsTheSharedBase()
    {
        // Attachment is INCOMPATIBLE (size retyped), so each version subclasses the shared base.
        typeof(Ignixa.Models.R4.Attachment).BaseType.ShouldBe(typeof(Attachment));
        typeof(Ignixa.Models.R5.Attachment).BaseType.ShouldBe(typeof(Attachment));
    }

    [Fact]
    public void GivenIncompatibleElement_WhenTyped_ThenAccessorReturnTypeDiffersAcrossVersions()
    {
        PropertyInfo r4Size = typeof(Ignixa.Models.R4.Attachment).GetProperty("Size")!;
        PropertyInfo r5Size = typeof(Ignixa.Models.R5.Attachment).GetProperty("Size")!;

        r4Size.ShouldNotBeNull();
        r5Size.ShouldNotBeNull();

        r4Size.PropertyType.ShouldBe(typeof(int?));
        r5Size.PropertyType.ShouldBe(typeof(long?));
        r4Size.PropertyType.ShouldNotBe(r5Size.PropertyType);
    }

    [Fact]
    public void GivenIncompatibleElement_WhenTyped_ThenItIsAbsentFromTheSharedBase()
    {
        // The INCOMPATIBLE element is omitted from the base so the base stays Liskov-substitutable.
        typeof(Attachment).GetProperty("Size").ShouldBeNull();
    }

    [Fact]
    public void GivenValueSetThatGainedCodesUnderTheSameUrl_WhenClassified_ThenBindingIsIncompatibleNotIdentical()
    {
        // Quantity.comparator is bound to http://hl7.org/fhir/ValueSet/quantity-comparator in both R4 and
        // R5 -- same URL, same binding strength -- but R5 added the "ad" (sufficient-to-achieve) code to
        // the SAME value set. Comparing signatures by URL alone would classify this Identical and build
        // the shared enum from R4's codes only, silently dropping "ad" to null on read for R5 callers.
        // The value-set-codes-hash in ElementSignature must catch this and demote it to Incompatible.
        PropertyInfo r4Comparator = typeof(Ignixa.Models.R4.Quantity).GetProperty("Comparator")!;
        PropertyInfo r5Comparator = typeof(Ignixa.Models.R5.Quantity).GetProperty("Comparator")!;

        r4Comparator.ShouldNotBeNull();
        r5Comparator.ShouldNotBeNull();

        r4Comparator.PropertyType.ShouldBe(typeof(Ignixa.Models.R4.QuantityComparator?));
        r5Comparator.PropertyType.ShouldBe(typeof(Ignixa.Models.R5.QuantityComparator?));
        r4Comparator.PropertyType.ShouldNotBe(r5Comparator.PropertyType);

        // Per-version enum, not a shared base enum: R5's Comparator has the extra "Ad" member R4 lacks.
        Enum.GetNames(typeof(Ignixa.Models.R5.QuantityComparator)).ShouldContain("Ad");
        Enum.GetNames(typeof(Ignixa.Models.R4.QuantityComparator)).ShouldNotContain("Ad");

        // Absent from the shared base so it stays Liskov-substitutable (mirrors Attachment.Size above).
        typeof(Quantity).GetProperty("Comparator").ShouldBeNull();
    }

    [Fact]
    public void GivenValueSetThatGainedManyCodesUnderTheSameUrl_WhenClassified_ThenRelatedArtifactTypeIsIncompatible()
    {
        // RelatedArtifact.type is the largest real-world instance of the same-URL-divergent-codes bug
        // this session's fix caught: R5 added ~25 new relationship codes (part-of, amends, cites, ...)
        // to http://hl7.org/fhir/related-artifact-type without changing the URL. A URL-only signature
        // would have silently capped every R5-only code at null forever.
        PropertyInfo r4Type = typeof(Ignixa.Models.R4.RelatedArtifact).GetProperty("Type")!;
        PropertyInfo r5Type = typeof(Ignixa.Models.R5.RelatedArtifact).GetProperty("Type")!;

        r4Type.PropertyType.ShouldNotBe(r5Type.PropertyType);

        string[] r4Codes = Enum.GetNames(typeof(Ignixa.Models.R4.RelatedArtifactType));
        string[] r5Codes = Enum.GetNames(typeof(Ignixa.Models.R5.RelatedArtifactType));
        r5Codes.Length.ShouldBeGreaterThan(r4Codes.Length);
        r5Codes.ShouldContain("PartOf");
        r4Codes.ShouldNotContain("PartOf");

        typeof(RelatedArtifact).GetProperty("Type").ShouldBeNull();
    }

    [Fact]
    public void GivenValueSetThatGainedOneCodeUnderTheSameUrl_WhenClassified_ThenDiscriminatorTypeIsIncompatible()
    {
        // ElementDefinitionSlicingDiscriminator.type: R5 added a single new code ("position") to
        // http://hl7.org/fhir/discriminator-type -- the minimal-diff case, as distinct from the
        // large RelatedArtifactType and single-narrow-domain QuantityComparator cases above.
        PropertyInfo r4Type = typeof(Ignixa.Models.R4.ElementDefinitionSlicingDiscriminator).GetProperty("Type")!;
        PropertyInfo r5Type = typeof(Ignixa.Models.R5.ElementDefinitionSlicingDiscriminator).GetProperty("Type")!;

        r4Type.PropertyType.ShouldNotBe(r5Type.PropertyType);

        Enum.GetNames(typeof(Ignixa.Models.R5.DiscriminatorType)).ShouldContain("Position");
        Enum.GetNames(typeof(Ignixa.Models.R4.DiscriminatorType)).ShouldNotContain("Position");

        typeof(ElementDefinitionSlicingDiscriminator).GetProperty("Type").ShouldBeNull();
    }

    [Fact]
    public void GivenAdditiveElement_WhenClassified_ThenItLivesOnlyOnTheVersionThatIntroducedIt()
    {
        // Observation.bodyStructure is ADDITIVE: introduced in R5, absent from R4. It must surface
        // on the R5 subclass only -- never promoted onto the shared base (that would lie to R4
        // callers) and never appear on the R4 subclass. This locks the Additive bucket, which the
        // Identical (base-only) and Incompatible (retyped) cases above do not cover.
        PropertyInfo? r5BodyStructure = typeof(Ignixa.Models.R5.Observation)
            .GetProperty("BodyStructure", BindingFlags.Public | BindingFlags.Instance);

        r5BodyStructure.ShouldNotBeNull();
        r5BodyStructure!.DeclaringType.ShouldBe(typeof(Ignixa.Models.R5.Observation));

        // Absent from the shared base (declared OR inherited).
        typeof(Observation).GetProperty("BodyStructure").ShouldBeNull();

        // Absent from the R4 subclass too (R4 Observation has no bodyStructure element).
        typeof(Ignixa.Models.R4.Observation).GetProperty("BodyStructure").ShouldBeNull();
    }
}
