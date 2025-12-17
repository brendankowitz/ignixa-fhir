// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;
using Ignixa.FhirFakes.Builders;
using Ignixa.Specification;
using Ignixa.Specification.Generated;
using Xunit;

namespace Ignixa.FhirFakes.Tests.Builders;

/// <summary>
/// Unit tests for GroupBuilder.
/// Tests group creation with various member configurations and properties.
/// </summary>
public class GroupBuilderTests
{
    private readonly IFhirSchemaProvider _schemaProvider = new R4CoreSchemaProvider();

    #region Basic Building Tests

    [Fact]
    public void GivenGroupBuilder_WhenBuildingWithDefaults_ThenCreatesGroupWithPersonTypeAndActualTrue()
    {
        // Arrange & Act
        var group = GroupBuilder.Create(_schemaProvider)
            .Build();

        // Assert
        group.ShouldNotBeNull();
        group.ResourceType.ShouldBe("Group");
        group.Id.ShouldNotBeNullOrEmpty();
        group.MutableNode["type"]?.GetValue<string>().ShouldBe("person");
        group.MutableNode["actual"]?.GetValue<bool>().ShouldBeTrue();
    }

    [Fact]
    public void GivenGroupBuilder_WhenBuildingWithId_ThenUsesProvidedId()
    {
        // Arrange
        var expectedId = "test-group-123";

        // Act
        var group = GroupBuilder.Create(_schemaProvider)
            .WithId(expectedId)
            .Build();

        // Assert
        group.Id.ShouldBe(expectedId);
    }

    [Fact]
    public void GivenGroupBuilder_WhenBuildingWithTag_ThenIncludesTagInMeta()
    {
        // Arrange
        var tag = "test-tag-456";

        // Act
        var group = GroupBuilder.Create(_schemaProvider)
            .WithTag(tag)
            .Build();

        // Assert
        var meta = group.MutableNode["meta"]?.AsObject();
        meta.ShouldNotBeNull();

        var tags = meta?["tag"]?.AsArray();
        tags.ShouldNotBeNull();
        tags!.Count.ShouldBe(1);

        var firstTag = tags?[0]?.AsObject();
        firstTag?["system"]?.GetValue<string>().ShouldBe("http://ignixa.dev/test-isolation");
        firstTag?["code"]?.GetValue<string>().ShouldBe(tag);
    }

    #endregion

    #region Type and Actual Tests

    [Fact]
    public void GivenGroupBuilder_WhenBuildingWithPersonType_ThenSetsTypeToPerson()
    {
        // Arrange & Act
        var group = GroupBuilder.Create(_schemaProvider)
            .WithType("person")
            .Build();

        // Assert
        group.MutableNode["type"]?.GetValue<string>().ShouldBe("person");
    }

    [Fact]
    public void GivenGroupBuilder_WhenBuildingWithPractitionerType_ThenSetsTypeToPractitioner()
    {
        // Arrange & Act
        var group = GroupBuilder.Create(_schemaProvider)
            .WithType("practitioner")
            .Build();

        // Assert
        group.MutableNode["type"]?.GetValue<string>().ShouldBe("practitioner");
    }

    [Fact]
    public void GivenGroupBuilder_WhenBuildingWithDeviceType_ThenSetsTypeToDevice()
    {
        // Arrange & Act
        var group = GroupBuilder.Create(_schemaProvider)
            .WithType("device")
            .Build();

        // Assert
        group.MutableNode["type"]?.GetValue<string>().ShouldBe("device");
    }

    [Fact]
    public void GivenGroupBuilder_WhenBuildingWithActualTrue_ThenSetsActualToTrue()
    {
        // Arrange & Act
        var group = GroupBuilder.Create(_schemaProvider)
            .WithActual(true)
            .Build();

        // Assert
        group.MutableNode["actual"]?.GetValue<bool>().ShouldBeTrue();
    }

    [Fact]
    public void GivenGroupBuilder_WhenBuildingWithActualFalse_ThenSetsActualToFalse()
    {
        // Arrange & Act
        var group = GroupBuilder.Create(_schemaProvider)
            .WithActual(false)
            .Build();

        // Assert
        group.MutableNode["actual"]?.GetValue<bool>().ShouldBeFalse();
    }

    #endregion

    #region Name Tests

    [Fact]
    public void GivenGroupBuilder_WhenBuildingWithName_ThenIncludesName()
    {
        // Arrange
        var name = "Diabetes Study Cohort";

        // Act
        var group = GroupBuilder.Create(_schemaProvider)
            .WithName(name)
            .Build();

        // Assert
        group.MutableNode["name"]?.GetValue<string>().ShouldBe(name);
    }

    [Fact]
    public void GivenGroupBuilder_WhenBuildingWithoutName_ThenNameIsNotPresent()
    {
        // Arrange & Act
        var group = GroupBuilder.Create(_schemaProvider)
            .Build();

        // Assert
        group.MutableNode.AsObject()["name"].ShouldBeNull();
    }

    #endregion

    #region Single Member Tests

    [Fact]
    public void GivenGroupBuilder_WhenAddingSinglePatientMember_ThenIncludesPatientInMembers()
    {
        // Arrange
        var patientId = "patient-123";

        // Act
        var group = GroupBuilder.Create(_schemaProvider)
            .WithType("person")
            .WithActual(true)
            .WithPatientMember(patientId)
            .Build();

        // Assert
        var members =group.MutableNode["member"]?.AsArray();
        members.ShouldNotBeNull();
        members!.Count.ShouldBe(1);

        var firstMember = members?[0]?.AsObject();
        var entity = firstMember?["entity"]?.AsObject();
        entity?["reference"]?.GetValue<string>().ShouldBe($"Patient/{patientId}");
    }

    [Fact]
    public void GivenGroupBuilder_WhenAddingSingleMemberWithResourceType_ThenIncludesMemberWithCorrectReference()
    {
        // Arrange
        var resourceType = "Practitioner";
        var id = "practitioner-456";

        // Act
        var group = GroupBuilder.Create(_schemaProvider)
            .WithType("practitioner")
            .WithMember(resourceType, id)
            .Build();

        // Assert
        var members =group.MutableNode["member"]?.AsArray();
        members.ShouldNotBeNull();
        members!.Count.ShouldBe(1);

        var firstMember = members?[0]?.AsObject();
        var entity = firstMember?["entity"]?.AsObject();
        entity?["reference"]?.GetValue<string>().ShouldBe($"{resourceType}/{id}");
    }

    #endregion

    #region Multiple Members Tests

    [Fact]
    public void GivenGroupBuilder_WhenAddingMultiplePatientMembers_ThenIncludesAllMembers()
    {
        // Arrange
        var patientId1 = "patient-1";
        var patientId2 = "patient-2";
        var patientId3 = "patient-3";

        // Act
        var group = GroupBuilder.Create(_schemaProvider)
            .WithType("person")
            .WithActual(true)
            .WithPatientMember(patientId1)
            .WithPatientMember(patientId2)
            .WithPatientMember(patientId3)
            .Build();

        // Assert
        var members =group.MutableNode["member"]?.AsArray();
        members.ShouldNotBeNull();
        members!.Count.ShouldBe(3);

        var references = members?
            .Select(m => m?["entity"]?["reference"]?.GetValue<string>())
            .ToList();

        references!.ShouldContain($"Patient/{patientId1}");
        references!.ShouldContain($"Patient/{patientId2}");
        references!.ShouldContain($"Patient/{patientId3}");
    }

    [Fact]
    public void GivenGroupBuilder_WhenAddingMembersViaParamsArray_ThenIncludesAllMembers()
    {
        // Arrange
        var patientIds = new[] { "patient-1", "patient-2", "patient-3", "patient-4" };

        // Act
        var group = GroupBuilder.Create(_schemaProvider)
            .WithType("person")
            .WithActual(true)
            .WithMembers(patientIds)
            .Build();

        // Assert
        var members =group.MutableNode["member"]?.AsArray();
        members.ShouldNotBeNull();
        members!.Count.ShouldBe(4);

        var references = members?
            .Select(m => m?["entity"]?["reference"]?.GetValue<string>())
            .ToList();

        references!.ShouldContain("Patient/patient-1");
        references!.ShouldContain("Patient/patient-2");
        references!.ShouldContain("Patient/patient-3");
        references!.ShouldContain("Patient/patient-4");
    }

    #endregion

    #region Mixed Resource Type Tests

    [Fact]
    public void GivenGroupBuilder_WhenAddingMixedResourceTypes_ThenIncludesAllWithCorrectReferences()
    {
        // Arrange
        var patientId = "patient-1";
        var practitionerId = "practitioner-1";
        var deviceId = "device-1";

        // Act
        var group = GroupBuilder.Create(_schemaProvider)
            .WithType("person")
            .WithActual(true)
            .WithMember("Patient", patientId)
            .WithMember("Practitioner", practitionerId)
            .WithMember("Device", deviceId)
            .Build();

        // Assert
        var members =group.MutableNode["member"]?.AsArray();
        members.ShouldNotBeNull();
        members!.Count.ShouldBe(3);

        var references = members?
            .Select(m => m?["entity"]?["reference"]?.GetValue<string>())
            .ToList();

        references!.ShouldContain($"Patient/{patientId}");
        references!.ShouldContain($"Practitioner/{practitionerId}");
        references!.ShouldContain($"Device/{deviceId}");
    }

    [Fact]
    public void GivenGroupBuilder_WhenMixingWithPatientMemberAndWithMember_ThenIncludesAll()
    {
        // Arrange
        var patientId1 = "patient-1";
        var patientId2 = "patient-2";
        var practitionerId = "practitioner-1";

        // Act
        var group = GroupBuilder.Create(_schemaProvider)
            .WithType("person")
            .WithPatientMember(patientId1)
            .WithMember("Practitioner", practitionerId)
            .WithPatientMember(patientId2)
            .Build();

        // Assert
        var members =group.MutableNode["member"]?.AsArray();
        members.ShouldNotBeNull();
        members!.Count.ShouldBe(3);

        var references = members?
            .Select(m => m?["entity"]?["reference"]?.GetValue<string>())
            .ToList();

        references!.ShouldContain($"Patient/{patientId1}");
        references!.ShouldContain($"Patient/{patientId2}");
        references!.ShouldContain($"Practitioner/{practitionerId}");
    }

    #endregion

    #region Empty Members Tests

    [Fact]
    public void GivenGroupBuilder_WhenBuildingWithoutMembers_ThenMemberArrayIsNotPresent()
    {
        // Arrange & Act
        var group = GroupBuilder.Create(_schemaProvider)
            .WithType("person")
            .WithActual(true)
            .Build();

        // Assert
        group.MutableNode.AsObject()["member"].ShouldBeNull();
    }

    #endregion

    #region Comprehensive Example Tests

    [Fact]
    public void GivenGroupBuilder_WhenBuildingCompleteGroup_ThenIncludesAllProperties()
    {
        // Arrange
        var tag = "test-tag";
        var groupId = "diabetes-cohort-1";
        var groupName = "Type 2 Diabetes Study Cohort";
        var patientIds = new[] { "patient-1", "patient-2", "patient-3" };

        // Act
        var group = GroupBuilder.Create(_schemaProvider)
            .WithId(groupId)
            .WithTag(tag)
            .WithType("person")
            .WithActual(true)
            .WithName(groupName)
            .WithMembers(patientIds)
            .Build();

        // Assert
        group.ShouldNotBeNull();
        group.ResourceType.ShouldBe("Group");
        group.Id.ShouldBe(groupId);
        group.MutableNode["type"]?.GetValue<string>().ShouldBe("person");
        group.MutableNode["actual"]?.GetValue<bool>().ShouldBeTrue();
        group.MutableNode["name"]?.GetValue<string>().ShouldBe(groupName);

        var members =group.MutableNode["member"]?.AsArray();
        members!.Count.ShouldBe(3);

        var meta = group.MutableNode["meta"]?.AsObject();
        var tags = meta?["tag"]?.AsArray();
        tags?[0]?["code"]?.GetValue<string>().ShouldBe(tag);
    }

    [Fact]
    public void GivenGroupBuilder_WhenBuildingDescriptiveGroup_ThenActualIsFalseAndNoMembers()
    {
        // Arrange & Act
        var group = GroupBuilder.Create(_schemaProvider)
            .WithType("person")
            .WithActual(false)
            .WithName("All diabetic patients over 65")
            .Build();

        // Assert
        group.MutableNode["actual"]?.GetValue<bool>().ShouldBeFalse();
        group.MutableNode["name"]?.GetValue<string>().ShouldBe("All diabetic patients over 65");
        group.MutableNode.AsObject()["member"].ShouldBeNull();
    }

    [Fact]
    public void GivenGroupBuilder_WhenBuildingPractitionerGroup_ThenCreatesValidPractitionerGroup()
    {
        // Arrange
        var practitionerIds = new[] { "prac-1", "prac-2" };

        // Act
        var group = GroupBuilder.Create(_schemaProvider)
            .WithType("practitioner")
            .WithActual(true)
            .WithName("Cardiology Team")
            .WithMember("Practitioner", practitionerIds[0])
            .WithMember("Practitioner", practitionerIds[1])
            .Build();

        // Assert
        group.MutableNode["type"]?.GetValue<string>().ShouldBe("practitioner");
        group.MutableNode["name"]?.GetValue<string>().ShouldBe("Cardiology Team");

        var members =group.MutableNode["member"]?.AsArray();
        members!.Count.ShouldBe(2);
    }

    #endregion

    #region Cross-Version Compatibility Tests

    [Fact]
    public void GivenGroupBuilder_WhenBuildingWithR4Schema_ThenCreatesValidR4Group()
    {
        // Arrange
        var r4Schema = new R4CoreSchemaProvider();

        // Act
        var group = GroupBuilder.Create(r4Schema)
            .WithType("person")
            .WithActual(true)
            .WithPatientMember("patient-1")
            .Build();

        // Assert
        group.ShouldNotBeNull();
        group.ResourceType.ShouldBe("Group");
    }

    [Fact]
    public void GivenGroupBuilder_WhenBuildingWithR4BSchema_ThenCreatesValidR4BGroup()
    {
        // Arrange
        var r4bSchema = new R4BCoreSchemaProvider();

        // Act
        var group = GroupBuilder.Create(r4bSchema)
            .WithType("person")
            .WithActual(true)
            .WithPatientMember("patient-1")
            .Build();

        // Assert
        group.ShouldNotBeNull();
        group.ResourceType.ShouldBe("Group");
    }

    [Fact]
    public void GivenGroupBuilder_WhenBuildingWithR5Schema_ThenCreatesValidR5Group()
    {
        // Arrange
        var r5Schema = new R5CoreSchemaProvider();

        // Act
        var group = GroupBuilder.Create(r5Schema)
            .WithType("person")
            .WithActual(true)
            .WithPatientMember("patient-1")
            .Build();

        // Assert
        group.ShouldNotBeNull();
        group.ResourceType.ShouldBe("Group");
    }

    #endregion
}
