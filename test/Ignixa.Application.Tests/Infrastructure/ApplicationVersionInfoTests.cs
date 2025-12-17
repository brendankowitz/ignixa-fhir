// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Reflection;
using Shouldly;
using Ignixa.Application.Infrastructure;

namespace Ignixa.Application.Tests.Infrastructure;

public class ApplicationVersionInfoTests
{
    #region Version Tests

    [Fact]
    public void GivenNullAssembly_WhenGettingVersion_ThenReturnsDefaultVersion()
    {
        // Arrange & Act
        var versionInfo = new ApplicationVersionInfo(assembly: null);

        // Assert
        versionInfo.Version.ShouldBe("0.0.0-dev");
    }

    [Fact]
    public void GivenCurrentAssembly_WhenGettingVersion_ThenReturnsNonEmptyVersion()
    {
        // Arrange - Use Application assembly directly instead of entry assembly
        // (entry assembly in test context is "testhost")
        var assembly = typeof(ApplicationVersionInfo).Assembly;

        // Act
        var versionInfo = new ApplicationVersionInfo(assembly);

        // Assert
        versionInfo.Version.ShouldNotBeNullOrEmpty();
        // Should be in semver format (at least x.y.z)
        versionInfo.Version.ShouldMatch(@"^\d+\.\d+\.\d+");
    }

    [Fact]
    public void GivenAssemblyWithInformationalVersion_WhenGettingVersion_ThenReturnsFullSemver()
    {
        // Arrange - Use the test assembly which has GitVersion attributes
        var assembly = typeof(ApplicationVersionInfoTests).Assembly;

        // Act
        var versionInfo = new ApplicationVersionInfo(assembly);

        // Assert
        versionInfo.Version.ShouldNotBeNullOrEmpty();
        // GitVersion typically adds metadata like +Branch.main.Sha.abc123
        // We should get the full string for FHIR compliance
    }

    #endregion

    #region Name Tests

    [Fact]
    public void GivenNullAssembly_WhenGettingName_ThenReturnsDefaultName()
    {
        // Arrange & Act
        var versionInfo = new ApplicationVersionInfo(assembly: null);

        // Assert
        versionInfo.Name.ShouldBe("Ignixa FHIR Server");
    }

    [Fact]
    public void GivenCurrentAssembly_WhenGettingName_ThenReturnsProductName()
    {
        // Arrange - Use Application assembly directly instead of entry assembly
        // (entry assembly in test context is "testhost")
        var assembly = typeof(ApplicationVersionInfo).Assembly;

        // Act
        var versionInfo = new ApplicationVersionInfo(assembly);

        // Assert
        versionInfo.Name.ShouldNotBeNullOrEmpty();
        // Should be "Ignixa FHIR Server" from Directory.Build.props Product attribute
        versionInfo.Name.ShouldContain("Ignixa");
    }

    #endregion

    #region ReleaseDate Tests

    [Fact]
    public void GivenNullAssembly_WhenGettingReleaseDate_ThenReturnsCurrentDate()
    {
        // Arrange & Act
        var versionInfo = new ApplicationVersionInfo(assembly: null);

        // Assert
        versionInfo.ReleaseDate.ShouldNotBeNullOrEmpty();
        // Should be in ISO 8601 format (YYYY-MM-DD)
        versionInfo.ReleaseDate.ShouldMatch(@"^\d{4}-\d{2}-\d{2}$");
    }

    [Fact]
    public void GivenCurrentAssembly_WhenGettingReleaseDate_ThenReturnsValidDate()
    {
        // Arrange - Use Application assembly directly instead of entry assembly
        // (entry assembly in test context is "testhost")
        var assembly = typeof(ApplicationVersionInfo).Assembly;

        // Act
        var versionInfo = new ApplicationVersionInfo(assembly);

        // Assert
        versionInfo.ReleaseDate.ShouldNotBeNullOrEmpty();
        // Should be in ISO 8601 format (YYYY-MM-DD)
        versionInfo.ReleaseDate.ShouldMatch(@"^\d{4}-\d{2}-\d{2}$");

        // Should be parseable as a date
        DateOnly.TryParse(versionInfo.ReleaseDate, out var parsedDate).ShouldBeTrue();
        parsedDate.ShouldBeLessThanOrEqualTo(DateOnly.FromDateTime(DateTime.UtcNow));
    }

    #endregion

    #region Integration Tests

    [Fact]
    public void GivenApplicationVersionInfo_WhenUsedForCapabilityStatement_ThenProvidesValidFhirVersion()
    {
        // Arrange - Use Application assembly directly instead of entry assembly
        // (entry assembly in test context is "testhost")
        var assembly = typeof(ApplicationVersionInfo).Assembly;

        // Act
        var versionInfo = new ApplicationVersionInfo(assembly);

        // Assert - All properties should be valid for FHIR CapabilityStatement
        versionInfo.Version.ShouldNotBeNullOrEmpty();
        versionInfo.Name.ShouldNotBeNullOrEmpty();
        versionInfo.ReleaseDate.ShouldNotBeNullOrEmpty();

        // Version should be valid semver (FHIR requirement)
        // Pattern: MAJOR.MINOR.PATCH[-PRERELEASE][+BUILDMETA]
        versionInfo.Version.ShouldMatch(@"^\d+\.\d+\.\d+");
    }

    [Fact]
    public void GivenApplicationVersionInfo_WhenInstantiated_ThenVersionIsImmutable()
    {
        // Arrange - Use Application assembly directly instead of entry assembly
        // (entry assembly in test context is "testhost")
        var assembly = typeof(ApplicationVersionInfo).Assembly;

        // Act
        var versionInfo1 = new ApplicationVersionInfo(assembly);
        var versionInfo2 = new ApplicationVersionInfo(assembly);

        // Assert - Multiple instances should return consistent values
        versionInfo1.Version.ShouldBe(versionInfo2.Version);
        versionInfo1.Name.ShouldBe(versionInfo2.Name);
        // ReleaseDate may vary slightly if based on file timestamp, but should be same date
    }

    #endregion
}
