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
    public void GivenIdenticalDatatype_WhenClassified_ThenQuantityIsBaseOnly()
    {
        typeof(Quantity).Namespace.ShouldBe("Ignixa.Models");

        R4Assembly.GetType("Ignixa.Models.R4.Quantity").ShouldBeNull();
        R5Assembly.GetType("Ignixa.Models.R5.Quantity").ShouldBeNull();
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
}
