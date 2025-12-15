// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using FluentAssertions;
using Ignixa.FhirFakes.Scenarios.Predefined;
using Ignixa.Specification.Generated;

namespace Ignixa.FhirFakes.Tests.Scenarios;

/// <summary>
/// Tests for the ComprehensivePreventiveCareScenario.
/// Validates pediatric well-child visits, adult annual physicals, and senior Medicare wellness visits.
/// </summary>
public sealed class ComprehensivePreventiveCareScenarioTests
{
    private readonly R4CoreSchemaProvider _schemaProvider = new();

    #region Pediatric Well-Child Visit Tests

    [Fact]
    public void GivenPediatricWellChildVisit_WhenGenerating12MonthVisit_ThenCreatesExpectedResources()
    {
        // Act
        var context = _schemaProvider.GetPediatricWellChildVisit(ageInMonths: 12, gender: "male");

        // Assert
        context.Should().NotBeNull();
        context.Patient.Should().NotBeNull();
        context.Patient!.ResourceType.Should().Be("Patient");
        context.Encounters.Should().ContainSingle();
        context.Encounters[0].ResourceType.Should().Be("Encounter");
        context.Observations.Should().NotBeEmpty();
        context.Immunizations.Should().HaveCountGreaterThanOrEqualTo(4);
        context.Procedures.Should().NotBeEmpty();
        context.Practitioners.Should().ContainSingle();
        context.Organizations.Should().ContainSingle();
    }

    [Fact]
    public void GivenPediatricWellChildVisit_When2MonthVisit_ThenIncludesMultipleImmunizations()
    {
        // Act
        var context = _schemaProvider.GetPediatricWellChildVisit(ageInMonths: 2, gender: "female");

        // Assert
        context.Immunizations.Should().HaveCountGreaterThanOrEqualTo(5);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(12)]
    [InlineData(60)]
    [InlineData(132)]
    public void GivenPediatricWellChildVisit_WhenVariousAges_ThenGeneratesSuccessfully(int ageInMonths)
    {
        // Act
        var context = _schemaProvider.GetPediatricWellChildVisit(ageInMonths, "male");

        // Assert
        context.Should().NotBeNull();
        context.Patient.Should().NotBeNull();
        context.Encounters.Should().ContainSingle();
        context.Immunizations.Should().NotBeEmpty();
    }

    [Fact]
    public void GivenPediatricWellChildVisit_WhenAgeOutOfRange_ThenThrowsArgumentOutOfRangeException()
    {
        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _schemaProvider.GetPediatricWellChildVisit(ageInMonths: 250, gender: "male"));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _schemaProvider.GetPediatricWellChildVisit(ageInMonths: -1, gender: "male"));
    }

    #endregion

    #region Adult Annual Physical Tests

    [Fact]
    public void GivenAdultAnnualPhysical_When45YearOldFemale_ThenIncludesAppropriateScreenings()
    {
        // Act
        var context = _schemaProvider.GetAdultAnnualPhysical(age: 45, gender: "female");

        // Assert
        context.Should().NotBeNull();
        context.Patient.Should().NotBeNull();
        context.Patient!.ResourceType.Should().Be("Patient");
        context.Encounters.Should().ContainSingle();
        context.Observations.Should().NotBeEmpty();
        context.DiagnosticReports.Should().HaveCountGreaterThanOrEqualTo(2);
        context.Procedures.Should().NotBeEmpty();
        context.Immunizations.Should().NotBeEmpty();
    }

    [Fact]
    public void GivenAdultAnnualPhysical_When55YearOldMale_ThenGeneratesSuccessfully()
    {
        // Act
        var context = _schemaProvider.GetAdultAnnualPhysical(age: 55, gender: "male");

        // Assert
        context.Should().NotBeNull();
        context.Observations.Should().NotBeEmpty();
        context.Procedures.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData(18)]
    [InlineData(35)]
    [InlineData(50)]
    [InlineData(64)]
    public void GivenAdultAnnualPhysical_WhenVariousAges_ThenGeneratesSuccessfully(int age)
    {
        // Act
        var context = _schemaProvider.GetAdultAnnualPhysical(age, "female");

        // Assert
        context.Should().NotBeNull();
        context.Patient.Should().NotBeNull();
        context.Encounters.Should().ContainSingle();
        context.DiagnosticReports.Should().NotBeEmpty();
    }

    [Fact]
    public void GivenAdultAnnualPhysical_WhenAgeOutOfRange_ThenThrowsArgumentOutOfRangeException()
    {
        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _schemaProvider.GetAdultAnnualPhysical(age: 17, gender: "female"));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _schemaProvider.GetAdultAnnualPhysical(age: 65, gender: "female"));
    }

    #endregion

    #region Senior Medicare Wellness Visit Tests

    [Fact]
    public void GivenSeniorMedicareWellnessVisit_When70YearOld_ThenIncludesGeriatricAssessments()
    {
        // Act
        var context = _schemaProvider.GetSeniorMedicareWellnessVisit(age: 70, gender: "female");

        // Assert
        context.Should().NotBeNull();
        context.Patient.Should().NotBeNull();
        context.Patient!.ResourceType.Should().Be("Patient");
        context.Encounters.Should().ContainSingle();
        context.Observations.Should().NotBeEmpty();
        context.DiagnosticReports.Should().HaveCountGreaterThanOrEqualTo(2);
        context.Immunizations.Should().HaveCountGreaterThanOrEqualTo(3);
        context.Procedures.Should().HaveCountGreaterThanOrEqualTo(3);
    }

    [Fact]
    public void GivenSeniorMedicareWellnessVisit_When80YearOldMale_ThenGeneratesSuccessfully()
    {
        // Act
        var context = _schemaProvider.GetSeniorMedicareWellnessVisit(age: 80, gender: "male");

        // Assert
        context.Should().NotBeNull();
        context.Observations.Should().NotBeEmpty();
        context.Immunizations.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData(65)]
    [InlineData(75)]
    [InlineData(85)]
    public void GivenSeniorMedicareWellnessVisit_WhenVariousAges_ThenGeneratesSuccessfully(int age)
    {
        // Act
        var context = _schemaProvider.GetSeniorMedicareWellnessVisit(age, "female");

        // Assert
        context.Should().NotBeNull();
        context.Patient.Should().NotBeNull();
        context.Encounters.Should().ContainSingle();
        context.Observations.Should().NotBeEmpty();
        context.Immunizations.Should().NotBeEmpty();
        context.Procedures.Should().NotBeEmpty();
    }

    [Fact]
    public void GivenSeniorMedicareWellnessVisit_WhenAgeUnder65_ThenThrowsArgumentOutOfRangeException()
    {
        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _schemaProvider.GetSeniorMedicareWellnessVisit(age: 64, gender: "female"));
    }

    #endregion
}
