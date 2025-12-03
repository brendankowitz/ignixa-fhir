// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using FluentAssertions;
using Ignixa.FhirFakes.Scenarios;
using Ignixa.FhirFakes.Scenarios.States;
using Ignixa.Specification.Generated;

namespace Ignixa.FhirFakes.Tests.States;

/// <summary>
/// Tests for CoverageState. Tests insurance coverage records with member IDs and payor references.
/// Uses AAA pattern and BDD naming convention.
/// </summary>
public class CoverageStateTests
{
    private readonly R4CoreSchemaProvider _schemaProvider = new();

    #region Basic Generation Tests

    [Fact]
    public void GivenCoverage_WhenGenerated_ThenCreatesCoverage()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddSelfCoverage()
            .Build();

        // Assert
        scenario.Coverages.Should().HaveCount(1);
        var coverage = scenario.Coverages[0];
        coverage.ResourceType.Should().Be("Coverage");
        coverage.Id.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GivenCoverage_WhenGenerated_ThenHasActiveStatus()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddSelfCoverage()
            .Build();

        // Assert
        var coverage = scenario.Coverages[0];
        var status = coverage.MutableNode["status"]?.GetValue<string>();
        status.Should().Be("active");
    }

    [Fact]
    public void GivenCoverage_WhenGenerated_ThenHasMemberId()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddSelfCoverage()
            .Build();

        // Assert
        var coverage = scenario.Coverages[0];
        var identifier = coverage.MutableNode["identifier"]?[0]?["value"]?.GetValue<string>();
        identifier.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GivenCoverageWithMemberId_WhenGenerated_ThenUsesProvidedMemberId()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddCoverage(memberId: "ABC123456789")
            .Build();

        // Assert
        var coverage = scenario.Coverages[0];
        var identifier = coverage.MutableNode["identifier"]?[0]?["value"]?.GetValue<string>();
        identifier.Should().Be("ABC123456789");
    }

    #endregion

    #region Patient Reference Tests

    [Fact]
    public void GivenCoverage_WhenGenerated_ThenReferencesBeneficiary()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddSelfCoverage()
            .Build();

        // Assert
        var coverage = scenario.Coverages[0];
        var beneficiaryRef = coverage.MutableNode["beneficiary"]?["reference"]?.GetValue<string>();
        beneficiaryRef.Should().Be($"Patient/{scenario.Patient!.Id}");
    }

    [Fact]
    public void GivenSelfCoverage_WhenGenerated_ThenReferencesPolicyHolder()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddSelfCoverage()
            .Build();

        // Assert
        var coverage = scenario.Coverages[0];
        var policyHolderRef = coverage.MutableNode["policyHolder"]?["reference"]?.GetValue<string>();
        policyHolderRef.Should().Be($"Patient/{scenario.Patient!.Id}");
    }

    [Fact]
    public void GivenCoverage_WhenGenerated_ThenReferencesSubscriber()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddSelfCoverage()
            .Build();

        // Assert
        var coverage = scenario.Coverages[0];
        var subscriberRef = coverage.MutableNode["subscriber"]?["reference"]?.GetValue<string>();
        subscriberRef.Should().Be($"Patient/{scenario.Patient!.Id}");
    }

    #endregion

    #region Relationship Tests

    [Fact]
    public void GivenSelfCoverage_WhenGenerated_ThenHasSelfRelationship()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddSelfCoverage()
            .Build();

        // Assert
        var coverage = scenario.Coverages[0];
        var relationshipCode = coverage.MutableNode["relationship"]?["coding"]?[0]?["code"]?.GetValue<string>();
        relationshipCode.Should().Be("self");
    }

    [Fact]
    public void GivenChildCoverage_WhenGenerated_ThenHasChildRelationship()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddChildCoverage()
            .Build();

        // Assert
        var coverage = scenario.Coverages[0];
        var relationshipCode = coverage.MutableNode["relationship"]?["coding"]?[0]?["code"]?.GetValue<string>();
        relationshipCode.Should().Be("child");
    }

    [Fact]
    public void GivenSpouseCoverage_WhenGenerated_ThenHasSpouseRelationship()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddCoverage(CoverageState.SpouseCoverage())
            .Build();

        // Assert
        var coverage = scenario.Coverages[0];
        var relationshipCode = coverage.MutableNode["relationship"]?["coding"]?[0]?["code"]?.GetValue<string>();
        relationshipCode.Should().Be("spouse");
    }

    #endregion

    #region Type Tests

    [Fact]
    public void GivenSelfCoverage_WhenGenerated_ThenHasExtendedHealthcareType()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddSelfCoverage()
            .Build();

        // Assert
        var coverage = scenario.Coverages[0];
        var typeCode = coverage.MutableNode["type"]?["coding"]?[0]?["code"]?.GetValue<string>();
        typeCode.Should().Be("EHCPOL");
    }

    [Fact]
    public void GivenMedicare_WhenGenerated_ThenHasPublicHealthcareType()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddMedicareCoverage()
            .Build();

        // Assert
        var coverage = scenario.Coverages[0];
        var typeCode = coverage.MutableNode["type"]?["coding"]?[0]?["code"]?.GetValue<string>();
        typeCode.Should().Be("PUBLICPOL");
    }

    [Fact]
    public void GivenMedicaid_WhenGenerated_ThenHasPublicHealthcareType()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddMedicaidCoverage()
            .Build();

        // Assert
        var coverage = scenario.Coverages[0];
        var typeCode = coverage.MutableNode["type"]?["coding"]?[0]?["code"]?.GetValue<string>();
        typeCode.Should().Be("PUBLICPOL");
    }

    [Fact]
    public void GivenDental_WhenGenerated_ThenHasDentalType()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddCoverage(CoverageState.Dental())
            .Build();

        // Assert
        var coverage = scenario.Coverages[0];
        var typeCode = coverage.MutableNode["type"]?["coding"]?[0]?["code"]?.GetValue<string>();
        typeCode.Should().Be("DENTAL");
    }

    [Fact]
    public void GivenVision_WhenGenerated_ThenHasVisionType()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddCoverage(CoverageState.Vision())
            .Build();

        // Assert
        var coverage = scenario.Coverages[0];
        var typeCode = coverage.MutableNode["type"]?["coding"]?[0]?["code"]?.GetValue<string>();
        typeCode.Should().Be("VISPOL");
    }

    #endregion

    #region Period Tests

    [Fact]
    public void GivenCoverage_WhenGenerated_ThenHasStartDate()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddSelfCoverage()
            .Build();

        // Assert
        var coverage = scenario.Coverages[0];
        var startDate = coverage.MutableNode["period"]?["start"]?.GetValue<string>();
        startDate.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GivenCoverageWithoutEndDate_WhenGenerated_ThenHasNoEndDate()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddSelfCoverage()
            .Build();

        // Assert
        var coverage = scenario.Coverages[0];
        var endDate = coverage.MutableNode["period"]?["end"];
        endDate.Should().BeNull();
    }

    [Fact]
    public void GivenCoverageWithEndDate_WhenGenerated_ThenHasEndDate()
    {
        // Arrange & Act
        var endDate = DateTime.UtcNow.AddYears(1);
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddCoverage(endDate: endDate)
            .Build();

        // Assert
        var coverage = scenario.Coverages[0];
        var actualEndDate = coverage.MutableNode["period"]?["end"]?.GetValue<string>();
        actualEndDate.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GivenCoverageWithStartDate_WhenGenerated_ThenUsesProvidedStartDate()
    {
        // Arrange & Act
        var startDate = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddCoverage(startDate: startDate)
            .Build();

        // Assert
        var coverage = scenario.Coverages[0];
        var actualStartDate = coverage.MutableNode["period"]?["start"]?.GetValue<string>();
        actualStartDate.Should().Be("2020-01-01");
    }

    #endregion

    #region Dependent Tests

    [Fact]
    public void GivenChildCoverageWithDependent_WhenGenerated_ThenHasDependentClass()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddChildCoverage(dependent: 2)
            .Build();

        // Assert
        var coverage = scenario.Coverages[0];
        var classArray = coverage.MutableNode["class"] as System.Text.Json.Nodes.JsonArray;
        classArray.Should().NotBeNull();

        var dependentClass = classArray!
            .FirstOrDefault(c => c?["type"]?["coding"]?[0]?["code"]?.GetValue<string>() == "subgroup");
        dependentClass.Should().NotBeNull();

        var dependentValue = dependentClass?["value"]?.GetValue<string>();
        dependentValue.Should().Be("2");
    }

    [Fact]
    public void GivenCoverageWithGroupId_WhenGenerated_ThenHasGroupClass()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddCoverage(new CoverageState
            {
                GroupId = "GROUP123"
            })
            .Build();

        // Assert
        var coverage = scenario.Coverages[0];
        var classArray = coverage.MutableNode["class"] as System.Text.Json.Nodes.JsonArray;
        classArray.Should().NotBeNull();

        var groupClass = classArray!
            .FirstOrDefault(c => c?["type"]?["coding"]?[0]?["code"]?.GetValue<string>() == "group");
        groupClass.Should().NotBeNull();

        var groupValue = groupClass?["value"]?.GetValue<string>();
        groupValue.Should().Be("GROUP123");
    }

    #endregion

    #region Payor Tests

    [Fact]
    public void GivenCoverageWithoutPayor_WhenGenerated_ThenHasGenericPayor()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddSelfCoverage()
            .Build();

        // Assert
        var coverage = scenario.Coverages[0];
        var payorArray = coverage.MutableNode["payor"] as System.Text.Json.Nodes.JsonArray;
        payorArray.Should().NotBeNull();
        payorArray!.Count.Should().Be(1);

        var display = payorArray[0]?["display"]?.GetValue<string>();
        display.Should().Be("Health Insurance Company");
    }

    #endregion

    #region Member ID Generation Tests

    [Fact]
    public void GivenCoverageWithoutMemberId_WhenGenerated_ThenGeneratesMemberId()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddSelfCoverage()
            .Build();

        // Assert
        var coverage = scenario.Coverages[0];
        var memberId = coverage.MutableNode["identifier"]?[0]?["value"]?.GetValue<string>();
        memberId.Should().NotBeNullOrEmpty();
        memberId!.Length.Should().Be(12); // 3 letters + 9 digits
    }

    [Fact]
    public void GivenMultipleCoverages_WhenGenerated_ThenEachHasUniqueMemberId()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddSelfCoverage()
            .AddCoverage(CoverageState.Dental())
            .AddCoverage(CoverageState.Vision())
            .Build();

        // Assert
        scenario.Coverages.Should().HaveCount(3);
        var memberIds = scenario.Coverages
            .Select(c => c.MutableNode["identifier"]?[0]?["value"]?.GetValue<string>())
            .ToList();
        memberIds.Should().OnlyHaveUniqueItems();
    }

    #endregion

    #region Factory Method Tests

    [Fact]
    public void GivenSelfCoverageFactory_WhenGenerated_ThenHasCorrectProperties()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddSelfCoverage()
            .Build();

        // Assert
        var coverage = scenario.Coverages[0];
        var relationshipCode = coverage.MutableNode["relationship"]?["coding"]?[0]?["code"]?.GetValue<string>();
        relationshipCode.Should().Be("self");

        var typeCode = coverage.MutableNode["type"]?["coding"]?[0]?["code"]?.GetValue<string>();
        typeCode.Should().Be("EHCPOL");
    }

    [Fact]
    public void GivenChildCoverageFactory_WhenGenerated_ThenHasCorrectProperties()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddChildCoverage(3)
            .Build();

        // Assert
        var coverage = scenario.Coverages[0];
        var relationshipCode = coverage.MutableNode["relationship"]?["coding"]?[0]?["code"]?.GetValue<string>();
        relationshipCode.Should().Be("child");

        var classArray = coverage.MutableNode["class"] as System.Text.Json.Nodes.JsonArray;
        var dependentValue = classArray?
            .FirstOrDefault(c => c?["type"]?["coding"]?[0]?["code"]?.GetValue<string>() == "subgroup")?
            ["value"]?.GetValue<string>();
        dependentValue.Should().Be("3");
    }

    [Fact]
    public void GivenMedicareFactory_WhenGenerated_ThenHasCorrectProperties()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddMedicareCoverage()
            .Build();

        // Assert
        var coverage = scenario.Coverages[0];
        var typeCode = coverage.MutableNode["type"]?["coding"]?[0]?["code"]?.GetValue<string>();
        typeCode.Should().Be("PUBLICPOL");

        var typeDisplay = coverage.MutableNode["type"]?["coding"]?[0]?["display"]?.GetValue<string>();
        typeDisplay.Should().Be("Public healthcare");
    }

    [Fact]
    public void GivenMedicaidFactory_WhenGenerated_ThenHasCorrectProperties()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddMedicaidCoverage()
            .Build();

        // Assert
        var coverage = scenario.Coverages[0];
        var typeCode = coverage.MutableNode["type"]?["coding"]?[0]?["code"]?.GetValue<string>();
        typeCode.Should().Be("PUBLICPOL");
    }

    #endregion

    #region Timeline and Context Tests

    [Fact]
    public void GivenMultipleCoverages_WhenGenerated_ThenAllAddedToContext()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddSelfCoverage()
            .AddCoverage(CoverageState.Dental())
            .AddCoverage(CoverageState.Vision())
            .Build();

        // Assert
        scenario.Coverages.Should().HaveCount(3);
    }

    [Fact]
    public void GivenCoverage_WhenGenerated_ThenAppearsInTimeline()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddSelfCoverage()
            .Build();

        // Assert
        var coverageEvents = scenario.Timeline.Where(e => e.EventType == "Coverage").ToList();
        coverageEvents.Should().HaveCount(1);
    }

    [Fact]
    public void GivenCoverage_WhenGenerated_ThenAppearsInAllResources()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddSelfCoverage()
            .Build();

        // Assert
        scenario.AllResources.Should().Contain(scenario.Coverages[0]);
    }

    [Fact]
    public void GivenCoverage_WhenGenerated_ThenSetsCurrentCoverage()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddSelfCoverage()
            .Build();

        // Assert
        scenario.CurrentCoverage.Should().NotBeNull();
        scenario.CurrentCoverage.Should().Be(scenario.Coverages[0]);
    }

    [Fact]
    public void GivenMultipleCoverages_WhenGenerated_ThenCurrentCoverageIsLast()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddSelfCoverage()
            .AddCoverage(CoverageState.Dental())
            .Build();

        // Assert
        scenario.CurrentCoverage.Should().Be(scenario.Coverages[1]);
    }

    #endregion

    #region Status Tests

    [Fact]
    public void GivenCoverageWithCancelledStatus_WhenGenerated_ThenHasCancelledStatus()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddCoverage(new CoverageState
            {
                Status = "cancelled"
            })
            .Build();

        // Assert
        var coverage = scenario.Coverages[0];
        var status = coverage.MutableNode["status"]?.GetValue<string>();
        status.Should().Be("cancelled");
    }

    [Fact]
    public void GivenCoverageWithDraftStatus_WhenGenerated_ThenHasDraftStatus()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddCoverage(new CoverageState
            {
                Status = "draft"
            })
            .Build();

        // Assert
        var coverage = scenario.Coverages[0];
        var status = coverage.MutableNode["status"]?.GetValue<string>();
        status.Should().Be("draft");
    }

    #endregion

    #region Edge Case Tests

    [Fact]
    public void GivenCoverageWithoutPatient_WhenGenerated_ThenThrowsException()
    {
        // Arrange
        var schemaProvider = new R4CoreSchemaProvider();
        var faker = new SchemaBasedFhirResourceFaker(schemaProvider);
        var context = new ScenarioContext();
        var state = CoverageState.SelfCoverage();

        // Act & Assert
        var act = () => state.Execute(context, faker);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Patient*");
    }

    [Fact]
    public void GivenCoverageWithSubscriberId_WhenGenerated_ThenUsesProvidedSubscriberId()
    {
        // Arrange & Act
        var scenario = new ScenarioBuilder(_schemaProvider)
            .WithPatient()
            .AddCoverage(new CoverageState
            {
                MemberId = "ABC123456789",
                SubscriberId = "SUB987654321"
            })
            .Build();

        // Assert
        var coverage = scenario.Coverages[0];
        var subscriberId = coverage.MutableNode["subscriber"]?["identifier"]?["value"]?.GetValue<string>();
        subscriberId.Should().Be("SUB987654321");
    }

    #endregion
}
