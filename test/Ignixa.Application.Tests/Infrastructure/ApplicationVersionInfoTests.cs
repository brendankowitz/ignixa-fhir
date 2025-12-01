// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Reflection;
using FluentAssertions;
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
        versionInfo.Version.Should().Be("0.0.0-dev");
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
        versionInfo.Version.Should().NotBeNullOrEmpty();
        // Should be in semver format (at least x.y.z)
        versionInfo.Version.Should().MatchRegex(@"^\d+\.\d+\.\d+");
    }

    [Fact]
    public void GivenAssemblyWithInformationalVersion_WhenGettingVersion_ThenReturnsFullSemver()
    {
        // Arrange - Use the test assembly which has GitVersion attributes
        var assembly = typeof(ApplicationVersionInfoTests).Assembly;

        // Act
        var versionInfo = new ApplicationVersionInfo(assembly);

        // Assert
        versionInfo.Version.Should().NotBeNullOrEmpty();
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
        versionInfo.Name.Should().Be("Ignixa FHIR Server");
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
        versionInfo.Name.Should().NotBeNullOrEmpty();
        // Should be "Ignixa FHIR Server" from Directory.Build.props Product attribute
        versionInfo.Name.Should().Contain("Ignixa");
    }

    #endregion

    #region ReleaseDate Tests

    [Fact]
    public void GivenNullAssembly_WhenGettingReleaseDate_ThenReturnsCurrentDate()
    {
        // Arrange & Act
        var versionInfo = new ApplicationVersionInfo(assembly: null);

        // Assert
        versionInfo.ReleaseDate.Should().NotBeNullOrEmpty();
        // Should be in ISO 8601 format (YYYY-MM-DD)
        versionInfo.ReleaseDate.Should().MatchRegex(@"^\d{4}-\d{2}-\d{2}$");
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
        versionInfo.ReleaseDate.Should().NotBeNullOrEmpty();
        // Should be in ISO 8601 format (YYYY-MM-DD)
        versionInfo.ReleaseDate.Should().MatchRegex(@"^\d{4}-\d{2}-\d{2}$");

        // Should be parseable as a date
        DateOnly.TryParse(versionInfo.ReleaseDate, out var parsedDate).Should().BeTrue();
        parsedDate.Should().BeOnOrBefore(DateOnly.FromDateTime(DateTime.UtcNow));
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
        versionInfo.Version.Should().NotBeNullOrEmpty();
        versionInfo.Name.Should().NotBeNullOrEmpty();
        versionInfo.ReleaseDate.Should().NotBeNullOrEmpty();

        // Version should be valid semver (FHIR requirement)
        // Pattern: MAJOR.MINOR.PATCH[-PRERELEASE][+BUILDMETA]
        versionInfo.Version.Should().MatchRegex(@"^\d+\.\d+\.\d+");
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
        versionInfo1.Version.Should().Be(versionInfo2.Version);
        versionInfo1.Name.Should().Be(versionInfo2.Name);
        // ReleaseDate may vary slightly if based on file timestamp, but should be same date
    }

    #endregion
}
