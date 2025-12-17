// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;
using Ignixa.FhirFakes.Scenarios;

namespace Ignixa.FhirFakes.Tests.Scenarios;

/// <summary>
/// Tests for the ResourceIdentity record.
/// Validates identity tracking for generated resources in scenarios.
/// </summary>
public class ResourceIdentityTests
{
    #region Constructor Tests

    [Fact]
    public void GivenValidParameters_WhenCreatingResourceIdentity_ThenPropertiesAreSet()
    {
        // Arrange
        var resourceType = "Patient";
        var id = "abc-123-def-456";
        var logicalName = "current-patient";

        // Act
        var identity = new ResourceIdentity(resourceType, id, logicalName);

        // Assert
        identity.ResourceType.ShouldBe(resourceType);
        identity.Id.ShouldBe(id);
        identity.LogicalName.ShouldBe(logicalName);
    }

    [Fact]
    public void GivenValidParametersWithoutLogicalName_WhenCreatingResourceIdentity_ThenLogicalNameIsNull()
    {
        // Arrange
        var resourceType = "Observation";
        var id = "obs-789";

        // Act
        var identity = new ResourceIdentity(resourceType, id);

        // Assert
        identity.ResourceType.ShouldBe(resourceType);
        identity.Id.ShouldBe(id);
        identity.LogicalName.ShouldBeNull();
    }

    [Fact]
    public void GivenExplicitNullLogicalName_WhenCreatingResourceIdentity_ThenLogicalNameIsNull()
    {
        // Arrange
        var resourceType = "Encounter";
        var id = "enc-456";

        // Act
        var identity = new ResourceIdentity(resourceType, id, null);

        // Assert
        identity.LogicalName.ShouldBeNull();
    }

    [Fact]
    public void GivenGuidId_WhenCreatingResourceIdentity_ThenIdIsPreserved()
    {
        // Arrange
        var resourceType = "Patient";
        var id = Guid.NewGuid().ToString();

        // Act
        var identity = new ResourceIdentity(resourceType, id);

        // Assert
        identity.Id.ShouldBe(id);
    }

    #endregion

    #region ResolvedReference Property Tests

    [Fact]
    public void GivenResourceIdentity_WhenGettingResolvedReference_ThenReturnsCorrectFormat()
    {
        // Arrange
        var identity = new ResourceIdentity("Patient", "abc-123");

        // Act
        var reference = identity.ResolvedReference;

        // Assert
        reference.ShouldBe("Patient/abc-123");
    }

    [Fact]
    public void GivenObservationIdentity_WhenGettingResolvedReference_ThenReturnsCorrectFormat()
    {
        // Arrange
        var identity = new ResourceIdentity("Observation", "obs-456-xyz");

        // Act
        var reference = identity.ResolvedReference;

        // Assert
        reference.ShouldBe("Observation/obs-456-xyz");
    }

    [Fact]
    public void GivenGuidId_WhenGettingResolvedReference_ThenFormatsCorrectly()
    {
        // Arrange
        var id = "a1b2c3d4-e5f6-7890-abcd-1234567890ab";
        var identity = new ResourceIdentity("Encounter", id);

        // Act
        var reference = identity.ResolvedReference;

        // Assert
        reference.ShouldBe($"Encounter/{id}");
    }

    #endregion

    #region UrnUuidReference Property Tests

    [Fact]
    public void GivenResourceIdentity_WhenGettingUrnUuidReference_ThenReturnsCorrectFormat()
    {
        // Arrange
        var id = "abc-123";
        var identity = new ResourceIdentity("Patient", id);

        // Act
        var reference = identity.UrnUuidReference;

        // Assert
        reference.ShouldBe("urn:uuid:abc-123");
    }

    [Fact]
    public void GivenGuidId_WhenGettingUrnUuidReference_ThenFormatsCorrectly()
    {
        // Arrange
        var id = "a1b2c3d4-e5f6-7890-abcd-1234567890ab";
        var identity = new ResourceIdentity("Observation", id);

        // Act
        var reference = identity.UrnUuidReference;

        // Assert
        reference.ShouldBe($"urn:uuid:{id}");
    }

    [Fact]
    public void GivenResourceIdentity_WhenGettingUrnUuidReference_ThenResourceTypeIsNotIncluded()
    {
        // Arrange
        var identity = new ResourceIdentity("Patient", "test-id");

        // Act
        var reference = identity.UrnUuidReference;

        // Assert
        reference.ShouldNotContain("Patient");
        reference.ShouldBe("urn:uuid:test-id");
    }

    #endregion

    #region GetReference Method Tests

    [Fact]
    public void GivenResourceIdentity_WhenGettingReferenceWithUrnUuidFormat_ThenReturnsUrnUuidReference()
    {
        // Arrange
        var id = "abc-123";
        var identity = new ResourceIdentity("Patient", id);

        // Act
        var reference = identity.GetReference(ReferenceFormat.UrnUuid);

        // Assert
        reference.ShouldBe("urn:uuid:abc-123");
        reference.ShouldBe(identity.UrnUuidReference);
    }

    [Fact]
    public void GivenResourceIdentity_WhenGettingReferenceWithResolvedFormat_ThenReturnsResolvedReference()
    {
        // Arrange
        var identity = new ResourceIdentity("Patient", "abc-123");

        // Act
        var reference = identity.GetReference(ReferenceFormat.Resolved);

        // Assert
        reference.ShouldBe("Patient/abc-123");
        reference.ShouldBe(identity.ResolvedReference);
    }

    [Fact]
    public void GivenResourceIdentity_WhenGettingReferenceWithInvalidFormat_ThenThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var identity = new ResourceIdentity("Patient", "abc-123");
        var invalidFormat = (ReferenceFormat)999;

        // Act
        var act = () => identity.GetReference(invalidFormat);

        // Assert
        var exception = Should.Throw<ArgumentOutOfRangeException>(act);
        exception.ParamName.ShouldBe("format");
    }

    [Theory]
    [InlineData("Patient", "p-123", ReferenceFormat.UrnUuid, "urn:uuid:p-123")]
    [InlineData("Patient", "p-123", ReferenceFormat.Resolved, "Patient/p-123")]
    [InlineData("Observation", "obs-456", ReferenceFormat.UrnUuid, "urn:uuid:obs-456")]
    [InlineData("Observation", "obs-456", ReferenceFormat.Resolved, "Observation/obs-456")]
    [InlineData("Encounter", "enc-789", ReferenceFormat.UrnUuid, "urn:uuid:enc-789")]
    [InlineData("Encounter", "enc-789", ReferenceFormat.Resolved, "Encounter/enc-789")]
    public void GivenVariousResourceTypes_WhenGettingReference_ThenReturnsExpectedFormat(
        string resourceType,
        string id,
        ReferenceFormat format,
        string expectedReference)
    {
        // Arrange
        var identity = new ResourceIdentity(resourceType, id);

        // Act
        var reference = identity.GetReference(format);

        // Assert
        reference.ShouldBe(expectedReference);
    }

    #endregion

    #region Record Equality Tests

    [Fact]
    public void GivenTwoIdenticalIdentities_WhenComparing_ThenAreEqual()
    {
        // Arrange
        var identity1 = new ResourceIdentity("Patient", "abc-123", "current-patient");
        var identity2 = new ResourceIdentity("Patient", "abc-123", "current-patient");

        // Act & Assert
        identity1.ShouldBe(identity2);
        (identity1 == identity2).ShouldBeTrue();
    }

    [Fact]
    public void GivenTwoDifferentIdentities_WhenComparing_ThenAreNotEqual()
    {
        // Arrange
        var identity1 = new ResourceIdentity("Patient", "abc-123");
        var identity2 = new ResourceIdentity("Patient", "xyz-789");

        // Act & Assert
        identity1.ShouldNotBe(identity2);
        (identity1 != identity2).ShouldBeTrue();
    }

    [Fact]
    public void GivenIdentitiesWithDifferentResourceTypes_WhenComparing_ThenAreNotEqual()
    {
        // Arrange
        var identity1 = new ResourceIdentity("Patient", "abc-123");
        var identity2 = new ResourceIdentity("Observation", "abc-123");

        // Act & Assert
        identity1.ShouldNotBe(identity2);
    }

    [Fact]
    public void GivenIdentitiesWithDifferentLogicalNames_WhenComparing_ThenAreNotEqual()
    {
        // Arrange
        var identity1 = new ResourceIdentity("Patient", "abc-123", "patient-1");
        var identity2 = new ResourceIdentity("Patient", "abc-123", "patient-2");

        // Act & Assert
        identity1.ShouldNotBe(identity2);
    }

    #endregion
}
