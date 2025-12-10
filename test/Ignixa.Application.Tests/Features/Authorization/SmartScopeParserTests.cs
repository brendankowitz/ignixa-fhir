// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using FluentAssertions;
using Ignixa.Application.Features.Authorization.Smart;

namespace Ignixa.Application.Tests.Features.Authorization;

public class SmartScopeParserTests
{
    [Theory]
    [InlineData("patient/Observation.rs", SmartScopeType.Patient, "Observation", "RS", SmartPermissions.Read | SmartPermissions.Search)]
    [InlineData("user/Patient.cruds", SmartScopeType.User, "Patient", "CRUDS", SmartPermissions.All)]
    [InlineData("system/Observation.r", SmartScopeType.System, "Observation", "R", SmartPermissions.Read)]
    [InlineData("patient/*.rs", SmartScopeType.Patient, "*", "RS", SmartPermissions.Read | SmartPermissions.Search)]
    [InlineData("user/*.cud", SmartScopeType.User, "*", "CUD", SmartPermissions.Create | SmartPermissions.Update | SmartPermissions.Delete)]
    [InlineData("practitioner/Schedule.rs", SmartScopeType.Practitioner, "Schedule", "RS", SmartPermissions.Read | SmartPermissions.Search)]
    public void ParseScope_ValidSmartV2Scopes_ReturnsCorrectSmartScope(
        string scopeString,
        SmartScopeType expectedType,
        string expectedResource,
        string expectedPermissionString,
        SmartPermissions expectedPermissions)
    {
        // Act
        var result = SmartScopeParser.ParseScope(scopeString);

        // Assert
        result.Should().NotBeNull();
        result!.Type.Should().Be(expectedType);
        result.ResourceType.Should().Be(expectedResource);
        result.PermissionString.Should().Be(expectedPermissionString);
        result.Permissions.Should().Be(expectedPermissions);
        result.OriginalScope.Should().Be(scopeString);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("invalid")]
    [InlineData("patient/Observation")]
    [InlineData("patient.rs")]
    [InlineData("foo/Observation.rs")]
    [InlineData("patient/Observation.sr")] // Wrong order - should be rs
    [InlineData("patient/Observation.dc")] // Wrong order - should be cd
    public void ParseScope_InvalidScopes_ReturnsNull(string scopeString)
    {
        // Act
        var result = SmartScopeParser.ParseScope(scopeString);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void ParseScopes_SpaceSeparatedString_ReturnsAllValidScopes()
    {
        // Arrange - SMART v2 format
        var scopeString = "patient/Observation.rs user/Patient.cruds invalid system/*.r";

        // Act
        var result = SmartScopeParser.ParseScopes(scopeString);

        // Assert
        result.Should().HaveCount(3);
        result[0].Type.Should().Be(SmartScopeType.Patient);
        result[1].Type.Should().Be(SmartScopeType.User);
        result[2].Type.Should().Be(SmartScopeType.System);
    }

    [Fact]
    public void ParseScopes_EmptyString_ReturnsEmptyList()
    {
        // Act
        var result = SmartScopeParser.ParseScopes(string.Empty);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void ParseScopes_NullString_ReturnsEmptyList()
    {
        // Act
        var result = SmartScopeParser.ParseScopes((string)null!);

        // Assert
        result.Should().BeEmpty();
    }

    [Theory]
    [InlineData("patient/Observation.rs", true)]
    [InlineData("patient/Observation.cruds", true)]
    [InlineData("invalid", false)]
    [InlineData("patient/Observation.read", true)] // v1 format now supported for backward compatibility
    public void IsValidSmartScope_ReturnsCorrectResult(string scope, bool expected)
    {
        // Act
        var result = SmartScopeParser.IsValidSmartScope(scope);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void ParseScope_WithSearchConstraints_ParsesCorrectly()
    {
        // Arrange - SMART v2 with search constraints
        var scopeString = "patient/Observation.rs?category=http://terminology.hl7.org/CodeSystem/observation-category|laboratory";

        // Act
        var result = SmartScopeParser.ParseScope(scopeString);

        // Assert
        result.Should().NotBeNull();
        result!.Type.Should().Be(SmartScopeType.Patient);
        result.ResourceType.Should().Be("Observation");
        result.Permissions.Should().Be(SmartPermissions.Read | SmartPermissions.Search);
        result.SearchConstraints.Should().NotBeNull();
        result.SearchConstraints!["category"].Should().Be("http://terminology.hl7.org/CodeSystem/observation-category|laboratory");
    }

    [Fact]
    public void BuildScope_CreatesValidSmartV2Scope()
    {
        // Act
        var scope = SmartScopeParser.BuildScope(
            SmartScopeType.Patient,
            "Observation",
            SmartPermissions.Read | SmartPermissions.Search);

        // Assert
        scope.Should().Be("patient/Observation.rs");
    }

    [Fact]
    public void BuildScope_WithConstraints_CreatesValidScope()
    {
        // Arrange
        var constraints = new Dictionary<string, string>
        {
            ["category"] = "laboratory"
        };

        // Act
        var scope = SmartScopeParser.BuildScope(
            SmartScopeType.Patient,
            "Observation",
            SmartPermissions.Read | SmartPermissions.Search,
            constraints);

        // Assert
        scope.Should().Be("patient/Observation.rs?category=laboratory");
    }

    #region SMART v1 Backward Compatibility Tests

    [Theory]
    [InlineData("patient/Observation.read", SmartScopeType.Patient, "Observation", "RS", SmartPermissions.Read | SmartPermissions.Search)]
    [InlineData("user/Patient.write", SmartScopeType.User, "Patient", "CUD", SmartPermissions.Create | SmartPermissions.Update | SmartPermissions.Delete)]
    [InlineData("system/*.*", SmartScopeType.System, "*", "CRUDS", SmartPermissions.All)]
    [InlineData("patient/Patient.read", SmartScopeType.Patient, "Patient", "RS", SmartPermissions.Read | SmartPermissions.Search)]
    [InlineData("user/*.write", SmartScopeType.User, "*", "CUD", SmartPermissions.Create | SmartPermissions.Update | SmartPermissions.Delete)]
    [InlineData("system/Observation.*", SmartScopeType.System, "Observation", "CRUDS", SmartPermissions.All)]
    public void ParseScope_ValidSmartV1Scopes_ConvertsToV2Format(
        string scopeString,
        SmartScopeType expectedType,
        string expectedResource,
        string expectedPermissionString,
        SmartPermissions expectedPermissions)
    {
        // Act
        var result = SmartScopeParser.ParseScope(scopeString);

        // Assert
        result.Should().NotBeNull();
        result!.Type.Should().Be(expectedType);
        result.ResourceType.Should().Be(expectedResource);
        result.PermissionString.Should().Be(expectedPermissionString);
        result.Permissions.Should().Be(expectedPermissions);
        result.OriginalScope.Should().Be(scopeString);
        result.SearchConstraints.Should().BeNull(); // v1 doesn't support search constraints
    }

    [Theory]
    [InlineData("patient/Observation.read")]
    [InlineData("PATIENT/OBSERVATION.READ")] // Case insensitive
    [InlineData("user/*.write")]
    [InlineData("system/Patient.*")]
    public void IsValidSmartScope_ValidV1Scopes_ReturnsTrue(string scope)
    {
        // Act
        var result = SmartScopeParser.IsValidSmartScope(scope);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void ParseScope_V1Read_GrantsReadAndSearchPermissions()
    {
        // Arrange - v1 "read" should grant both read and search
        var scope = "patient/Observation.read";

        // Act
        var result = SmartScopeParser.ParseScope(scope);

        // Assert
        result.Should().NotBeNull();
        result!.HasPermission(SmartPermissions.Read).Should().BeTrue();
        result.HasPermission(SmartPermissions.Search).Should().BeTrue();
        result.HasPermission(SmartPermissions.Create).Should().BeFalse();
        result.HasPermission(SmartPermissions.Update).Should().BeFalse();
        result.HasPermission(SmartPermissions.Delete).Should().BeFalse();
    }

    [Fact]
    public void ParseScope_V1Write_GrantsCreateUpdateDeletePermissions()
    {
        // Arrange - v1 "write" should grant create, update, delete
        var scope = "user/Patient.write";

        // Act
        var result = SmartScopeParser.ParseScope(scope);

        // Assert
        result.Should().NotBeNull();
        result!.HasPermission(SmartPermissions.Create).Should().BeTrue();
        result!.HasPermission(SmartPermissions.Update).Should().BeTrue();
        result!.HasPermission(SmartPermissions.Delete).Should().BeTrue();
        result.HasPermission(SmartPermissions.Read).Should().BeFalse();
        result.HasPermission(SmartPermissions.Search).Should().BeFalse();
    }

    [Fact]
    public void ParseScope_V1Wildcard_GrantsAllPermissions()
    {
        // Arrange - v1 "*" should grant all CRUDS permissions
        var scope = "system/*.*";

        // Act
        var result = SmartScopeParser.ParseScope(scope);

        // Assert
        result.Should().NotBeNull();
        result!.Permissions.Should().Be(SmartPermissions.All);
        result.HasPermission(SmartPermissions.Create).Should().BeTrue();
        result.HasPermission(SmartPermissions.Read).Should().BeTrue();
        result.HasPermission(SmartPermissions.Update).Should().BeTrue();
        result.HasPermission(SmartPermissions.Delete).Should().BeTrue();
        result.HasPermission(SmartPermissions.Search).Should().BeTrue();
    }

    [Fact]
    public void ParseScopes_MixedV1AndV2_ParsesBoth()
    {
        // Arrange - Mix of v1 and v2 scope formats
        var scopeString = "patient/Observation.read user/Patient.cruds system/*.* invalid patient/Medication.rs";

        // Act
        var result = SmartScopeParser.ParseScopes(scopeString);

        // Assert
        result.Should().HaveCount(4);

        // v1: patient/Observation.read → patient/Observation.rs
        result[0].Type.Should().Be(SmartScopeType.Patient);
        result[0].ResourceType.Should().Be("Observation");
        result[0].PermissionString.Should().Be("RS");

        // v2: user/Patient.cruds
        result[1].Type.Should().Be(SmartScopeType.User);
        result[1].ResourceType.Should().Be("Patient");
        result[1].PermissionString.Should().Be("CRUDS");

        // v1: system/*.* → system/*.cruds
        result[2].Type.Should().Be(SmartScopeType.System);
        result[2].ResourceType.Should().Be("*");
        result[2].PermissionString.Should().Be("CRUDS");

        // v2: patient/Medication.rs
        result[3].Type.Should().Be(SmartScopeType.Patient);
        result[3].ResourceType.Should().Be("Medication");
        result[3].PermissionString.Should().Be("RS");
    }

    [Theory]
    [InlineData("practitioner/Schedule.read")] // v1 doesn't support practitioner context
    [InlineData("patient/Observation.read?category=lab")] // v1 doesn't support search constraints
    [InlineData("patient/Observation.execute")] // Invalid v1 permission
    public void ParseScope_InvalidV1Variations_ReturnsNull(string scope)
    {
        // Act
        var result = SmartScopeParser.ParseScope(scope);

        // Assert
        result.Should().BeNull();
    }

    #endregion
}
