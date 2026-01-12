// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using System.Text.Json.Nodes;
using Shouldly;
using Ignixa.FhirFakes.Builders;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;
using Ignixa.Specification.Generated;
using Xunit;

namespace Ignixa.FhirFakes.Tests.Builders;

/// <summary>
/// Unit tests for CareTeamBuilder.
/// Verifies fluent API, participant management, and resource structure.
/// </summary>
public class CareTeamBuilderTests
{
    private readonly IFhirSchemaProvider _schemaProvider = new R4CoreSchemaProvider();

    #region Basic Building Tests

    [Fact]
    public void GivenBasicCareTeam_WhenBuilt_ThenHasCorrectStructure()
    {
        var careTeam = CareTeamBuilder.Create(_schemaProvider)
            .WithName("Primary Care Team")
            .Build();

        careTeam.ShouldNotBeNull();
        careTeam.ResourceType.ShouldBe("CareTeam");
        careTeam.Id.ShouldNotBeNullOrEmpty();

        var node = careTeam.MutableNode;
        node["status"]?.GetValue<string>().ShouldBe("active");
        node["name"]?.GetValue<string>().ShouldBe("Primary Care Team");
    }

    [Fact]
    public void GivenCareTeamWithId_WhenBuilt_ThenHasSpecifiedId()
    {
        var expectedId = "ct-12345";

        var careTeam = CareTeamBuilder.Create(_schemaProvider)
            .WithId(expectedId)
            .Build();

        careTeam.Id.ShouldBe(expectedId);
    }

    [Fact]
    public void GivenCareTeamWithTag_WhenBuilt_ThenHasTag()
    {
        var tag = Guid.NewGuid().ToString();

        var careTeam = CareTeamBuilder.Create(_schemaProvider)
            .WithTag(tag)
            .Build();

        var node = careTeam.MutableNode;
        var metaTags = node["meta"]?["tag"] as JsonArray;

        metaTags.ShouldNotBeNull();
        metaTags.Count.ShouldBe(1);

        var tagObj = metaTags![0] as JsonObject;
        tagObj!["system"]?.GetValue<string>().ShouldBe("http://ignixa.dev/test-isolation");
        tagObj["code"]?.GetValue<string>().ShouldBe(tag);
    }

    #endregion

    #region Status Tests

    [Theory]
    [InlineData("active")]
    [InlineData("suspended")]
    [InlineData("inactive")]
    [InlineData("proposed")]
    [InlineData("entered-in-error")]
    public void GivenCareTeamWithStatus_WhenBuilt_ThenHasCorrectStatus(string status)
    {
        var careTeam = CareTeamBuilder.Create(_schemaProvider)
            .WithStatus(status)
            .Build();

        var node = careTeam.MutableNode;
        node["status"]?.GetValue<string>().ShouldBe(status);
    }

    [Fact]
    public void GivenCareTeamWithoutStatus_WhenBuilt_ThenHasDefaultActiveStatus()
    {
        var careTeam = CareTeamBuilder.Create(_schemaProvider)
            .Build();

        var node = careTeam.MutableNode;
        node["status"]?.GetValue<string>().ShouldBe("active");
    }

    #endregion

    #region Subject Reference Tests

    [Fact]
    public void GivenCareTeamWithSubject_WhenBuilt_ThenHasSubjectReference()
    {
        var patientId = "patient-123";

        var careTeam = CareTeamBuilder.Create(_schemaProvider)
            .WithSubject(patientId)
            .Build();

        var node = careTeam.MutableNode;
        var subject = node["subject"] as JsonObject;

        subject.ShouldNotBeNull();
        subject!["reference"]?.GetValue<string>().ShouldBe($"Patient/{patientId}");
    }

    [Fact]
    public void GivenCareTeamWithoutSubject_WhenBuilt_ThenHasNoSubject()
    {
        var careTeam = CareTeamBuilder.Create(_schemaProvider)
            .Build();

        var node = careTeam.MutableNode;
        node["subject"].ShouldBeNull();
    }

    #endregion

    #region Single Participant Tests

    [Fact]
    public void GivenCareTeamWithSingleParticipant_WhenBuilt_ThenHasParticipant()
    {
        var practitionerId = "practitioner-123";

        var careTeam = CareTeamBuilder.Create(_schemaProvider)
            .WithParticipant("Practitioner", practitionerId)
            .Build();

        var node = careTeam.MutableNode;
        var participants = node["participant"] as JsonArray;

        participants.ShouldNotBeNull();
        participants.Count.ShouldBe(1);

        var participant = participants![0] as JsonObject;
        var member = participant!["member"] as JsonObject;
        member!["reference"]?.GetValue<string>().ShouldBe($"Practitioner/{practitionerId}");
    }

    [Fact]
    public void GivenCareTeamWithParticipantWithRole_WhenBuilt_ThenHasRoleInParticipant()
    {
        var practitionerId = "practitioner-123";
        var roleCode = "223366009"; // Healthcare professional

        var careTeam = CareTeamBuilder.Create(_schemaProvider)
            .WithParticipant("Practitioner", practitionerId, roleCode)
            .Build();

        var node = careTeam.MutableNode;
        var participants = node["participant"] as JsonArray;
        var participant = participants![0] as JsonObject;
        var roles = participant!["role"] as JsonArray;

        roles.ShouldNotBeNull();
        roles.Count.ShouldBe(1);

        var role = roles![0] as JsonObject;
        var codings = role!["coding"] as JsonArray;
        var coding = codings![0] as JsonObject;

        coding!["system"]?.GetValue<string>().ShouldBe("http://snomed.info/sct");
        coding["code"]?.GetValue<string>().ShouldBe(roleCode);
    }

    [Fact]
    public void GivenCareTeamWithParticipantWithoutRole_WhenBuilt_ThenHasNoRoleInParticipant()
    {
        var organizationId = "org-123";

        var careTeam = CareTeamBuilder.Create(_schemaProvider)
            .WithParticipant("Organization", organizationId)
            .Build();

        var node = careTeam.MutableNode;
        var participants = node["participant"] as JsonArray;
        var participant = participants![0] as JsonObject;

        participant!["role"].ShouldBeNull();
    }

    [Fact]
    public void GivenCareTeamWithCustomRoleSystem_WhenBuilt_ThenUsesCustomSystem()
    {
        var practitionerId = "practitioner-123";
        var roleCode = "CARDIOLOGIST";
        var customSystem = "http://hospital.example.org/roles";

        var careTeam = CareTeamBuilder.Create(_schemaProvider)
            .WithParticipant("Practitioner", practitionerId, roleCode, customSystem)
            .Build();

        var node = careTeam.MutableNode;
        var participants = node["participant"] as JsonArray;
        var participant = participants![0] as JsonObject;
        var roles = participant!["role"] as JsonArray;
        var role = roles![0] as JsonObject;
        var codings = role!["coding"] as JsonArray;
        var coding = codings![0] as JsonObject;

        coding!["system"]?.GetValue<string>().ShouldBe(customSystem);
        coding["code"]?.GetValue<string>().ShouldBe(roleCode);
    }

    #endregion

    #region Multiple Participants Tests

    [Fact]
    public void GivenCareTeamWithMultipleParticipants_WhenBuilt_ThenHasAllParticipants()
    {
        var practitionerId1 = "practitioner-1";
        var practitionerId2 = "practitioner-2";
        var organizationId = "org-1";

        var careTeam = CareTeamBuilder.Create(_schemaProvider)
            .WithParticipant("Practitioner", practitionerId1)
            .WithParticipant("Practitioner", practitionerId2)
            .WithParticipant("Organization", organizationId)
            .Build();

        var node = careTeam.MutableNode;
        var participants = node["participant"] as JsonArray;

        participants.ShouldNotBeNull();
        participants.Count.ShouldBe(3);

        var references = participants!.Select(p =>
            ((JsonObject)p!)["member"]!.AsObject()["reference"]?.GetValue<string>()).ToList();

        references.ShouldContain($"Practitioner/{practitionerId1}");
        references.ShouldContain($"Practitioner/{practitionerId2}");
        references.ShouldContain($"Organization/{organizationId}");
    }

    [Fact]
    public void GivenCareTeamWithMixedRoles_WhenBuilt_ThenHasCorrectRoles()
    {
        var practitionerId = "practitioner-1";
        var organizationId = "org-1";

        var careTeam = CareTeamBuilder.Create(_schemaProvider)
            .WithParticipant("Practitioner", practitionerId, "223366009")
            .WithParticipant("Organization", organizationId) // No role
            .Build();

        var node = careTeam.MutableNode;
        var participants = node["participant"] as JsonArray;

        // First participant should have role
        var participant1 = participants![0] as JsonObject;
        participant1!["role"].ShouldNotBeNull();

        // Second participant should not have role
        var participant2 = participants[1] as JsonObject;
        participant2!["role"].ShouldBeNull();
    }

    #endregion

    #region Convenience Methods Tests

    [Fact]
    public void GivenCareTeamWithPatientParticipant_WhenBuilt_ThenHasPatientReference()
    {
        var patientId = "patient-123";

        var careTeam = CareTeamBuilder.Create(_schemaProvider)
            .WithPatientParticipant(patientId)
            .Build();

        var node = careTeam.MutableNode;
        var participants = node["participant"] as JsonArray;
        var participant = participants![0] as JsonObject;
        var member = participant!["member"] as JsonObject;

        member!["reference"]?.GetValue<string>().ShouldBe($"Patient/{patientId}");
    }

    [Fact]
    public void GivenCareTeamWithPractitionerParticipant_WhenBuilt_ThenHasDefaultRole()
    {
        var practitionerId = "practitioner-123";

        var careTeam = CareTeamBuilder.Create(_schemaProvider)
            .WithPractitionerParticipant(practitionerId)
            .Build();

        var node = careTeam.MutableNode;
        var participants = node["participant"] as JsonArray;
        var participant = participants![0] as JsonObject;
        var roles = participant!["role"] as JsonArray;
        var role = roles![0] as JsonObject;
        var codings = role!["coding"] as JsonArray;
        var coding = codings![0] as JsonObject;

        coding!["system"]?.GetValue<string>().ShouldBe("http://snomed.info/sct");
        coding["code"]?.GetValue<string>().ShouldBe("223366009"); // Default Healthcare professional
    }

    [Fact]
    public void GivenCareTeamWithPractitionerParticipantWithCustomRole_WhenBuilt_ThenUsesCustomRole()
    {
        var practitionerId = "practitioner-123";
        var customRole = "17561000"; // Cardiologist

        var careTeam = CareTeamBuilder.Create(_schemaProvider)
            .WithPractitionerParticipant(practitionerId, customRole)
            .Build();

        var node = careTeam.MutableNode;
        var participants = node["participant"] as JsonArray;
        var participant = participants![0] as JsonObject;
        var roles = participant!["role"] as JsonArray;
        var role = roles![0] as JsonObject;
        var codings = role!["coding"] as JsonArray;
        var coding = codings![0] as JsonObject;

        coding!["code"]?.GetValue<string>().ShouldBe(customRole);
    }

    [Fact]
    public void GivenCareTeamWithOrganizationParticipant_WhenBuilt_ThenHasOrganizationReference()
    {
        var organizationId = "org-123";

        var careTeam = CareTeamBuilder.Create(_schemaProvider)
            .WithOrganizationParticipant(organizationId)
            .Build();

        var node = careTeam.MutableNode;
        var participants = node["participant"] as JsonArray;
        var participant = participants![0] as JsonObject;
        var member = participant!["member"] as JsonObject;

        member!["reference"]?.GetValue<string>().ShouldBe($"Organization/{organizationId}");
    }

    [Fact]
    public void GivenCareTeamWithPatientParticipantWithRole_WhenBuilt_ThenHasRole()
    {
        var patientId = "patient-123";
        var roleCode = "116154003"; // Patient

        var careTeam = CareTeamBuilder.Create(_schemaProvider)
            .WithPatientParticipant(patientId, roleCode)
            .Build();

        var node = careTeam.MutableNode;
        var participants = node["participant"] as JsonArray;
        var participant = participants![0] as JsonObject;
        var roles = participant!["role"] as JsonArray;

        roles.ShouldNotBeNull();
        roles.Count.ShouldBe(1);

        var role = roles![0] as JsonObject;
        var codings = role!["coding"] as JsonArray;
        var coding = codings![0] as JsonObject;

        coding!["code"]?.GetValue<string>().ShouldBe(roleCode);
    }

    [Fact]
    public void GivenCareTeamWithOrganizationParticipantWithRole_WhenBuilt_ThenHasRole()
    {
        var organizationId = "org-123";
        var roleCode = "394658006"; // Clinical specialty

        var careTeam = CareTeamBuilder.Create(_schemaProvider)
            .WithOrganizationParticipant(organizationId, roleCode)
            .Build();

        var node = careTeam.MutableNode;
        var participants = node["participant"] as JsonArray;
        var participant = participants![0] as JsonObject;
        var roles = participant!["role"] as JsonArray;

        roles.ShouldNotBeNull();
        roles.Count.ShouldBe(1);
    }

    #endregion

    #region Mixed Resource Types Tests

    [Fact]
    public void GivenCareTeamWithMixedResourceTypes_WhenBuilt_ThenHasAllTypes()
    {
        var patientId = "patient-123";
        var practitionerId = "practitioner-123";
        var organizationId = "org-123";

        var careTeam = CareTeamBuilder.Create(_schemaProvider)
            .WithName("Multi-Disciplinary Team")
            .WithSubject(patientId)
            .WithPatientParticipant(patientId)
            .WithPractitionerParticipant(practitionerId, "223366009")
            .WithOrganizationParticipant(organizationId)
            .Build();

        var node = careTeam.MutableNode;
        var participants = node["participant"] as JsonArray;

        participants!.Count.ShouldBe(3);

        var references = participants!.Select(p =>
            ((JsonObject)p!)["member"]!.AsObject()["reference"]?.GetValue<string>()).ToList();

        references.ShouldContain($"Patient/{patientId}");
        references.ShouldContain($"Practitioner/{practitionerId}");
        references.ShouldContain($"Organization/{organizationId}");
    }

    #endregion

    #region Fluent API Tests

    [Fact]
    public void GivenCareTeamWithAllFeatures_WhenBuilt_ThenHasAllProperties()
    {
        var tag = Guid.NewGuid().ToString();
        var careTeamId = "ct-comprehensive";
        var patientId = "patient-123";
        var practitionerId1 = "practitioner-1";
        var practitionerId2 = "practitioner-2";
        var organizationId = "org-1";

        var careTeam = CareTeamBuilder.Create(_schemaProvider)
            .WithId(careTeamId)
            .WithTag(tag)
            .WithName("Comprehensive Care Team")
            .WithStatus("active")
            .WithSubject(patientId)
            .WithPatientParticipant(patientId)
            .WithPractitionerParticipant(practitionerId1, "17561000") // Cardiologist
            .WithPractitionerParticipant(practitionerId2) // Default role
            .WithOrganizationParticipant(organizationId)
            .Build();

        careTeam.Id.ShouldBe(careTeamId);

        var node = careTeam.MutableNode;
        node["status"]?.GetValue<string>().ShouldBe("active");
        node["name"]?.GetValue<string>().ShouldBe("Comprehensive Care Team");

        var subject = node["subject"] as JsonObject;
        subject!["reference"]?.GetValue<string>().ShouldBe($"Patient/{patientId}");

        var participants = node["participant"] as JsonArray;
        participants!.Count.ShouldBe(4);

        var metaTags = node["meta"]?["tag"] as JsonArray;
        var tagObj = metaTags![0] as JsonObject;
        tagObj!["code"]?.GetValue<string>().ShouldBe(tag);
    }

    #endregion

    #region Validation Tests

    [Fact]
    public void GivenNullSchemaProvider_WhenCreating_ThenThrowsArgumentNullException()
    {
        var act = () => CareTeamBuilder.Create(null!);

        var exception = Should.Throw<ArgumentNullException>(act);
        exception.ParamName.ShouldBe("schemaProvider");
    }

    [Fact]
    public void GivenNullStatus_WhenSettingStatus_ThenThrowsArgumentNullException()
    {
        var builder = CareTeamBuilder.Create(_schemaProvider);

        var act = () => builder.WithStatus(null!);

        var exception = Should.Throw<ArgumentNullException>(act);
        exception.ParamName.ShouldBe("status");
    }

    [Fact]
    public void GivenNullName_WhenSettingName_ThenThrowsArgumentNullException()
    {
        var builder = CareTeamBuilder.Create(_schemaProvider);

        var act = () => builder.WithName(null!);

        var exception = Should.Throw<ArgumentNullException>(act);
        exception.ParamName.ShouldBe("name");
    }

    [Fact]
    public void GivenNullPatientId_WhenSettingSubject_ThenThrowsArgumentNullException()
    {
        var builder = CareTeamBuilder.Create(_schemaProvider);

        var act = () => builder.WithSubject(null!);

        var exception = Should.Throw<ArgumentNullException>(act);
        exception.ParamName.ShouldBe("patientId");
    }

    [Fact]
    public void GivenNullResourceType_WhenAddingParticipant_ThenThrowsArgumentNullException()
    {
        var builder = CareTeamBuilder.Create(_schemaProvider);

        var act = () => builder.WithParticipant(null!, "id-123");

        var exception = Should.Throw<ArgumentNullException>(act);
        exception.ParamName.ShouldBe("resourceType");
    }

    [Fact]
    public void GivenNullId_WhenAddingParticipant_ThenThrowsArgumentNullException()
    {
        var builder = CareTeamBuilder.Create(_schemaProvider);

        var act = () => builder.WithParticipant("Practitioner", null!);

        var exception = Should.Throw<ArgumentNullException>(act);
        exception.ParamName.ShouldBe("id");
    }

    [Fact]
    public void GivenNullPatientId_WhenAddingPatientParticipant_ThenThrowsArgumentNullException()
    {
        var builder = CareTeamBuilder.Create(_schemaProvider);

        var act = () => builder.WithPatientParticipant(null!);

        var exception = Should.Throw<ArgumentNullException>(act);
        exception.ParamName.ShouldBe("patientId");
    }

    [Fact]
    public void GivenNullPractitionerId_WhenAddingPractitionerParticipant_ThenThrowsArgumentNullException()
    {
        var builder = CareTeamBuilder.Create(_schemaProvider);

        var act = () => builder.WithPractitionerParticipant(null!);

        var exception = Should.Throw<ArgumentNullException>(act);
        exception.ParamName.ShouldBe("practitionerId");
    }

    [Fact]
    public void GivenNullOrganizationId_WhenAddingOrganizationParticipant_ThenThrowsArgumentNullException()
    {
        var builder = CareTeamBuilder.Create(_schemaProvider);

        var act = () => builder.WithOrganizationParticipant(null!);

        var exception = Should.Throw<ArgumentNullException>(act);
        exception.ParamName.ShouldBe("organizationId");
    }

    #endregion
}
