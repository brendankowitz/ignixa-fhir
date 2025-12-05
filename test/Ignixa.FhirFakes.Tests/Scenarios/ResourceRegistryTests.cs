// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using FluentAssertions;
using Ignixa.FhirFakes.Scenarios;

namespace Ignixa.FhirFakes.Tests.Scenarios;

/// <summary>
/// Tests for the ResourceRegistry class.
/// Validates registration and lookup of resource identities.
/// </summary>
public class ResourceRegistryTests
{
    #region Register Tests

    [Fact]
    public void GivenNewRegistry_WhenRegisteringResource_ThenResourceIsRegistered()
    {
        // Arrange
        var registry = new ResourceRegistry();
        var identity = new ResourceIdentity("Patient", "patient-123");

        // Act
        registry.Register(identity);

        // Assert
        registry.Count.Should().Be(1);
    }

    [Fact]
    public void GivenRegistry_WhenRegisteringResourceWithLogicalName_ThenResourceIsRetrievableByBothIdAndName()
    {
        // Arrange
        var registry = new ResourceRegistry();
        var identity = new ResourceIdentity("Patient", "patient-123", "current-patient");

        // Act
        registry.Register(identity);

        // Assert
        registry.GetById("patient-123").Should().Be(identity);
        registry.GetByLogicalName("current-patient").Should().Be(identity);
    }

    [Fact]
    public void GivenRegistry_WhenRegisteringNullIdentity_ThenThrowsArgumentNullException()
    {
        // Arrange
        var registry = new ResourceRegistry();

        // Act
        var act = () => registry.Register(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("identity");
    }

    [Fact]
    public void GivenRegistry_WhenRegisteringMultipleResources_ThenAllAreRegistered()
    {
        // Arrange
        var registry = new ResourceRegistry();
        var patient = new ResourceIdentity("Patient", "patient-1", "patient");
        var encounter = new ResourceIdentity("Encounter", "encounter-1", "current-encounter");
        var observation = new ResourceIdentity("Observation", "obs-1");

        // Act
        registry.Register(patient);
        registry.Register(encounter);
        registry.Register(observation);

        // Assert
        registry.Count.Should().Be(3);
        registry.GetById("patient-1").Should().Be(patient);
        registry.GetById("encounter-1").Should().Be(encounter);
        registry.GetById("obs-1").Should().Be(observation);
    }

    [Fact]
    public void GivenRegistry_WhenRegisteringResourceWithSameId_ThenOverwritesExisting()
    {
        // Arrange
        var registry = new ResourceRegistry();
        var original = new ResourceIdentity("Patient", "patient-123", "original");
        var updated = new ResourceIdentity("Patient", "patient-123", "updated");

        // Act
        registry.Register(original);
        registry.Register(updated);

        // Assert
        registry.Count.Should().Be(1);
        registry.GetById("patient-123").Should().Be(updated);
        registry.GetByLogicalName("updated").Should().Be(updated);
    }

    [Fact]
    public void GivenRegistry_WhenRegisteringResourceWithoutLogicalName_ThenOnlyIdIsSearchable()
    {
        // Arrange
        var registry = new ResourceRegistry();
        var identity = new ResourceIdentity("Observation", "obs-123");

        // Act
        registry.Register(identity);

        // Assert
        registry.GetById("obs-123").Should().Be(identity);
        // No logical name was provided, so no name-based lookup
    }

    #endregion

    #region GetById Tests

    [Fact]
    public void GivenRegistryWithResource_WhenGettingById_ThenReturnsCorrectIdentity()
    {
        // Arrange
        var registry = new ResourceRegistry();
        var identity = new ResourceIdentity("Patient", "patient-456");
        registry.Register(identity);

        // Act
        var result = registry.GetById("patient-456");

        // Assert
        result.Should().Be(identity);
    }

    [Fact]
    public void GivenRegistryWithResource_WhenGettingByUnknownId_ThenReturnsNull()
    {
        // Arrange
        var registry = new ResourceRegistry();
        var identity = new ResourceIdentity("Patient", "patient-456");
        registry.Register(identity);

        // Act
        var result = registry.GetById("unknown-id");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void GivenEmptyRegistry_WhenGettingById_ThenReturnsNull()
    {
        // Arrange
        var registry = new ResourceRegistry();

        // Act
        var result = registry.GetById("any-id");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void GivenMultipleResourcesWithDifferentTypes_WhenGettingByIdForEach_ThenReturnsCorrectIdentities()
    {
        // Arrange
        var registry = new ResourceRegistry();
        var patient = new ResourceIdentity("Patient", "patient-1");
        var encounter = new ResourceIdentity("Encounter", "encounter-1");
        var observation = new ResourceIdentity("Observation", "obs-1");
        registry.Register(patient);
        registry.Register(encounter);
        registry.Register(observation);

        // Act & Assert
        registry.GetById("patient-1").Should().Be(patient);
        registry.GetById("encounter-1").Should().Be(encounter);
        registry.GetById("obs-1").Should().Be(observation);
    }

    #endregion

    #region GetByLogicalName Tests

    [Fact]
    public void GivenRegistryWithNamedResource_WhenGettingByLogicalName_ThenReturnsCorrectIdentity()
    {
        // Arrange
        var registry = new ResourceRegistry();
        var identity = new ResourceIdentity("Patient", "patient-789", "current-patient");
        registry.Register(identity);

        // Act
        var result = registry.GetByLogicalName("current-patient");

        // Assert
        result.Should().Be(identity);
    }

    [Fact]
    public void GivenRegistryWithNamedResource_WhenGettingByUnknownName_ThenReturnsNull()
    {
        // Arrange
        var registry = new ResourceRegistry();
        var identity = new ResourceIdentity("Patient", "patient-789", "current-patient");
        registry.Register(identity);

        // Act
        var result = registry.GetByLogicalName("unknown-name");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void GivenEmptyRegistry_WhenGettingByLogicalName_ThenReturnsNull()
    {
        // Arrange
        var registry = new ResourceRegistry();

        // Act
        var result = registry.GetByLogicalName("any-name");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void GivenMultipleNamedResources_WhenGettingByLogicalName_ThenReturnsCorrectIdentities()
    {
        // Arrange
        var registry = new ResourceRegistry();
        var patient = new ResourceIdentity("Patient", "p-1", "patient");
        var encounter = new ResourceIdentity("Encounter", "e-1", "current-encounter");
        var practitioner = new ResourceIdentity("Practitioner", "pr-1", "current-practitioner");
        registry.Register(patient);
        registry.Register(encounter);
        registry.Register(practitioner);

        // Act & Assert
        registry.GetByLogicalName("patient").Should().Be(patient);
        registry.GetByLogicalName("current-encounter").Should().Be(encounter);
        registry.GetByLogicalName("current-practitioner").Should().Be(practitioner);
    }

    #endregion

    #region All Property Tests

    [Fact]
    public void GivenEmptyRegistry_WhenAccessingAll_ThenReturnsEmptyDictionary()
    {
        // Arrange
        var registry = new ResourceRegistry();

        // Act
        var all = registry.All;

        // Assert
        all.Should().BeEmpty();
    }

    [Fact]
    public void GivenRegistryWithResources_WhenAccessingAll_ThenReturnsAllRegisteredIdentities()
    {
        // Arrange
        var registry = new ResourceRegistry();
        var patient = new ResourceIdentity("Patient", "patient-1");
        var encounter = new ResourceIdentity("Encounter", "encounter-1");
        registry.Register(patient);
        registry.Register(encounter);

        // Act
        var all = registry.All;

        // Assert
        all.Should().HaveCount(2);
        all.Should().ContainKey("patient-1");
        all.Should().ContainKey("encounter-1");
        all["patient-1"].Should().Be(patient);
        all["encounter-1"].Should().Be(encounter);
    }

    [Fact]
    public void GivenRegistryWithResources_WhenAccessingAll_ThenReturnsReadOnlyDictionary()
    {
        // Arrange
        var registry = new ResourceRegistry();
        registry.Register(new ResourceIdentity("Patient", "patient-1"));

        // Act
        var all = registry.All;

        // Assert
        all.Should().BeAssignableTo<IReadOnlyDictionary<string, ResourceIdentity>>();
    }

    #endregion

    #region Count Property Tests

    [Fact]
    public void GivenEmptyRegistry_WhenAccessingCount_ThenReturnsZero()
    {
        // Arrange
        var registry = new ResourceRegistry();

        // Act
        var count = registry.Count;

        // Assert
        count.Should().Be(0);
    }

    [Fact]
    public void GivenRegistryWithResources_WhenAccessingCount_ThenReturnsCorrectCount()
    {
        // Arrange
        var registry = new ResourceRegistry();
        registry.Register(new ResourceIdentity("Patient", "p-1"));
        registry.Register(new ResourceIdentity("Encounter", "e-1"));
        registry.Register(new ResourceIdentity("Observation", "o-1"));

        // Act
        var count = registry.Count;

        // Assert
        count.Should().Be(3);
    }

    [Fact]
    public void GivenRegistryWithOverwrittenResource_WhenAccessingCount_ThenCountDoesNotIncrease()
    {
        // Arrange
        var registry = new ResourceRegistry();
        registry.Register(new ResourceIdentity("Patient", "p-1", "original"));
        registry.Register(new ResourceIdentity("Patient", "p-1", "updated"));

        // Act
        var count = registry.Count;

        // Assert
        count.Should().Be(1);
    }

    #endregion

    #region Clear Tests

    [Fact]
    public void GivenRegistryWithResources_WhenClearing_ThenRegistryIsEmpty()
    {
        // Arrange
        var registry = new ResourceRegistry();
        registry.Register(new ResourceIdentity("Patient", "p-1", "patient"));
        registry.Register(new ResourceIdentity("Encounter", "e-1", "encounter"));
        registry.Register(new ResourceIdentity("Observation", "o-1"));

        // Act
        registry.Clear();

        // Assert
        registry.Count.Should().Be(0);
        registry.All.Should().BeEmpty();
    }

    [Fact]
    public void GivenRegistryWithResources_WhenClearing_ThenIdLookupReturnsNull()
    {
        // Arrange
        var registry = new ResourceRegistry();
        registry.Register(new ResourceIdentity("Patient", "p-1"));
        registry.Register(new ResourceIdentity("Encounter", "e-1"));

        // Act
        registry.Clear();

        // Assert
        registry.GetById("p-1").Should().BeNull();
        registry.GetById("e-1").Should().BeNull();
    }

    [Fact]
    public void GivenRegistryWithNamedResources_WhenClearing_ThenLogicalNameLookupReturnsNull()
    {
        // Arrange
        var registry = new ResourceRegistry();
        registry.Register(new ResourceIdentity("Patient", "p-1", "patient"));
        registry.Register(new ResourceIdentity("Encounter", "e-1", "current-encounter"));

        // Act
        registry.Clear();

        // Assert
        registry.GetByLogicalName("patient").Should().BeNull();
        registry.GetByLogicalName("current-encounter").Should().BeNull();
    }

    [Fact]
    public void GivenEmptyRegistry_WhenClearing_ThenNoExceptionIsThrown()
    {
        // Arrange
        var registry = new ResourceRegistry();

        // Act
        var act = () => registry.Clear();

        // Assert
        act.Should().NotThrow();
        registry.Count.Should().Be(0);
    }

    [Fact]
    public void GivenClearedRegistry_WhenRegisteringNewResources_ThenResourcesAreRegistered()
    {
        // Arrange
        var registry = new ResourceRegistry();
        registry.Register(new ResourceIdentity("Patient", "old-1"));
        registry.Clear();

        var newIdentity = new ResourceIdentity("Patient", "new-1", "patient");

        // Act
        registry.Register(newIdentity);

        // Assert
        registry.Count.Should().Be(1);
        registry.GetById("new-1").Should().Be(newIdentity);
        registry.GetByLogicalName("patient").Should().Be(newIdentity);
    }

    #endregion

    #region Edge Case Tests

    [Fact]
    public void GivenResourceWithEmptyStringLogicalName_WhenRegistering_ThenNotSearchableByName()
    {
        // Arrange
        var registry = new ResourceRegistry();
        var identity = new ResourceIdentity("Patient", "p-1", "");

        // Act
        registry.Register(identity);

        // Assert
        registry.GetById("p-1").Should().Be(identity);
        // Empty string should not be registered as a logical name
    }

    [Fact]
    public void GivenGuidIds_WhenRegisteringAndLookingUp_ThenWorksCorrectly()
    {
        // Arrange
        var registry = new ResourceRegistry();
        var guid1 = Guid.NewGuid().ToString();
        var guid2 = Guid.NewGuid().ToString();
        var patient = new ResourceIdentity("Patient", guid1, "patient");
        var encounter = new ResourceIdentity("Encounter", guid2, "encounter");

        // Act
        registry.Register(patient);
        registry.Register(encounter);

        // Assert
        registry.GetById(guid1).Should().Be(patient);
        registry.GetById(guid2).Should().Be(encounter);
        registry.GetByLogicalName("patient").Should().Be(patient);
        registry.GetByLogicalName("encounter").Should().Be(encounter);
    }

    [Fact]
    public void GivenSameLogicalNameForDifferentResources_WhenRegisteringSequentially_ThenLastOneWins()
    {
        // Arrange
        var registry = new ResourceRegistry();
        var patient1 = new ResourceIdentity("Patient", "p-1", "current");
        var patient2 = new ResourceIdentity("Patient", "p-2", "current");

        // Act
        registry.Register(patient1);
        registry.Register(patient2);

        // Assert
        registry.GetByLogicalName("current").Should().Be(patient2);
        registry.GetById("p-1").Should().Be(patient1);
        registry.GetById("p-2").Should().Be(patient2);
    }

    #endregion
}
