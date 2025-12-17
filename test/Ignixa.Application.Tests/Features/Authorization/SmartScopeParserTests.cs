// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;
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
        result.ShouldNotBeNull();
        result!.Type.ShouldBe(expectedType);
        result.ResourceType.ShouldBe(expectedResource);
        result.PermissionString.ShouldBe(expectedPermissionString);
        result.Permissions.ShouldBe(expectedPermissions);
        result.OriginalScope.ShouldBe(scopeString);
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
        result.ShouldBeNull();
    }

    [Fact]
    public void ParseScopes_SpaceSeparatedString_ReturnsAllValidScopes()
    {
        // Arrange - SMART v2 format
        var scopeString = "patient/Observation.rs user/Patient.cruds invalid system/*.r";

        // Act
        var result = SmartScopeParser.ParseScopes(scopeString);

        // Assert
        result.Count.ShouldBe(3);
        result[0].Type.ShouldBe(SmartScopeType.Patient);
        result[1].Type.ShouldBe(SmartScopeType.User);
        result[2].Type.ShouldBe(SmartScopeType.System);
    }

    [Fact]
    public void ParseScopes_EmptyString_ReturnsEmptyList()
    {
        // Act
        var result = SmartScopeParser.ParseScopes(string.Empty);

        // Assert
        result.ShouldBeEmpty();
    }

    [Fact]
    public void ParseScopes_NullString_ReturnsEmptyList()
    {
        // Act
        var result = SmartScopeParser.ParseScopes((string)null!);

        // Assert
        result.ShouldBeEmpty();
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
        result.ShouldBe(expected);
    }

    [Fact]
    public void ParseScope_WithSearchConstraints_ParsesCorrectly()
    {
        // Arrange - SMART v2 with search constraints
        var scopeString = "patient/Observation.rs?category=http://terminology.hl7.org/CodeSystem/observation-category|laboratory";

        // Act
        var result = SmartScopeParser.ParseScope(scopeString);

        // Assert
        result.ShouldNotBeNull();
        result!.Type.ShouldBe(SmartScopeType.Patient);
        result.ResourceType.ShouldBe("Observation");
        result.Permissions.ShouldBe(SmartPermissions.Read | SmartPermissions.Search);
        result.SearchConstraints.ShouldNotBeNull();
        result.SearchConstraints!["category"].ShouldBe("http://terminology.hl7.org/CodeSystem/observation-category|laboratory");
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
        scope.ShouldBe("patient/Observation.rs");
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
        scope.ShouldBe("patient/Observation.rs?category=laboratory");
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
        result.ShouldNotBeNull();
        result!.Type.ShouldBe(expectedType);
        result.ResourceType.ShouldBe(expectedResource);
        result.PermissionString.ShouldBe(expectedPermissionString);
        result.Permissions.ShouldBe(expectedPermissions);
        result.OriginalScope.ShouldBe(scopeString);
        result.SearchConstraints.ShouldBeNull(); // v1 doesn't support search constraints
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
        result.ShouldBeTrue();
    }

    [Fact]
    public void ParseScope_V1Read_GrantsReadAndSearchPermissions()
    {
        // Arrange - v1 "read" should grant both read and search
        var scope = "patient/Observation.read";

        // Act
        var result = SmartScopeParser.ParseScope(scope);

        // Assert
        result.ShouldNotBeNull();
        result!.HasPermission(SmartPermissions.Read).ShouldBeTrue();
        result.HasPermission(SmartPermissions.Search).ShouldBeTrue();
        result.HasPermission(SmartPermissions.Create).ShouldBeFalse();
        result.HasPermission(SmartPermissions.Update).ShouldBeFalse();
        result.HasPermission(SmartPermissions.Delete).ShouldBeFalse();
    }

    [Fact]
    public void ParseScope_V1Write_GrantsCreateUpdateDeletePermissions()
    {
        // Arrange - v1 "write" should grant create, update, delete
        var scope = "user/Patient.write";

        // Act
        var result = SmartScopeParser.ParseScope(scope);

        // Assert
        result.ShouldNotBeNull();
        result!.HasPermission(SmartPermissions.Create).ShouldBeTrue();
        result!.HasPermission(SmartPermissions.Update).ShouldBeTrue();
        result!.HasPermission(SmartPermissions.Delete).ShouldBeTrue();
        result.HasPermission(SmartPermissions.Read).ShouldBeFalse();
        result.HasPermission(SmartPermissions.Search).ShouldBeFalse();
    }

    [Fact]
    public void ParseScope_V1Wildcard_GrantsAllPermissions()
    {
        // Arrange - v1 "*" should grant all CRUDS permissions
        var scope = "system/*.*";

        // Act
        var result = SmartScopeParser.ParseScope(scope);

        // Assert
        result.ShouldNotBeNull();
        result!.Permissions.ShouldBe(SmartPermissions.All);
        result.HasPermission(SmartPermissions.Create).ShouldBeTrue();
        result.HasPermission(SmartPermissions.Read).ShouldBeTrue();
        result.HasPermission(SmartPermissions.Update).ShouldBeTrue();
        result.HasPermission(SmartPermissions.Delete).ShouldBeTrue();
        result.HasPermission(SmartPermissions.Search).ShouldBeTrue();
    }

    [Fact]
    public void ParseScopes_MixedV1AndV2_ParsesBoth()
    {
        // Arrange - Mix of v1 and v2 scope formats
        var scopeString = "patient/Observation.read user/Patient.cruds system/*.* invalid patient/Medication.rs";

        // Act
        var result = SmartScopeParser.ParseScopes(scopeString);

        // Assert
        result.Count.ShouldBe(4);

        // v1: patient/Observation.read → patient/Observation.rs
        result[0].Type.ShouldBe(SmartScopeType.Patient);
        result[0].ResourceType.ShouldBe("Observation");
        result[0].PermissionString.ShouldBe("RS");

        // v2: user/Patient.cruds
        result[1].Type.ShouldBe(SmartScopeType.User);
        result[1].ResourceType.ShouldBe("Patient");
        result[1].PermissionString.ShouldBe("CRUDS");

        // v1: system/*.* → system/*.cruds
        result[2].Type.ShouldBe(SmartScopeType.System);
        result[2].ResourceType.ShouldBe("*");
        result[2].PermissionString.ShouldBe("CRUDS");

        // v2: patient/Medication.rs
        result[3].Type.ShouldBe(SmartScopeType.Patient);
        result[3].ResourceType.ShouldBe("Medication");
        result[3].PermissionString.ShouldBe("RS");
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
        result.ShouldBeNull();
    }

    #endregion
}
