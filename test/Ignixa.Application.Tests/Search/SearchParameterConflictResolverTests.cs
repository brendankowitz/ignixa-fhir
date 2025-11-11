// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using Ignixa.Search.Definition;
using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ignixa.Application.Tests.Search;

/// <summary>
/// Tests for SearchParameterConflictResolver ensuring deterministic multi-IG conflict resolution.
/// Verifies that the same IG always wins regardless of load order (deterministic behavior).
/// </summary>
public class SearchParameterConflictResolverTests
{
    #region Priority-Based Resolution Tests

    [Fact]
    public void GivenPriorityConfiguration_WhenMultipleIGsDefineParameter_ThenHighestPriorityWins()
    {
        // Arrange
        var options = new SearchParameterResolutionOptions
        {
            PackagePriorityOrder = new List<string> { "hl7.fhir.us.core", "hl7.fhir.au.base" },
            LogConflicts = false
        };
        var resolver = new SearchParameterConflictResolver(options, NullLogger<SearchParameterConflictResolver>.Instance);

        var usCoreParam = CreateSearchParameter("race", "http://hl7.org/fhir/us/core/SearchParameter/us-core-patient-race");
        var auBaseParam = CreateSearchParameter("race", "http://hl7.org/fhir/au/base/SearchParameter/au-base-patient-race");

        var metadata = new Dictionary<string, PackageMetadata>
        {
            [usCoreParam.Url.ToString()] = new PackageMetadata
            {
                PackageId = "hl7.fhir.us.core",
                PackageVersion = "5.0.1",
                LoadedDate = DateTimeOffset.UtcNow
            },
            [auBaseParam.Url.ToString()] = new PackageMetadata
            {
                PackageId = "hl7.fhir.au.base",
                PackageVersion = "4.1.0",
                LoadedDate = DateTimeOffset.UtcNow
            }
        };

        // Act
        var winner = resolver.ResolveConflict(
            new[] { usCoreParam, auBaseParam },
            "race",
            "Patient",
            metadata);

        // Assert
        Assert.Equal(usCoreParam.Url, winner.Url);
    }

    [Fact]
    public void GivenPriorityConfiguration_WhenLoadOrderChanges_ThenResultStaysDeterministic()
    {
        // Arrange
        var options = new SearchParameterResolutionOptions
        {
            PackagePriorityOrder = new List<string> { "hl7.fhir.us.core", "hl7.fhir.au.base", "hl7.fhir.hl7v2" },
            LogConflicts = false
        };
        var resolver = new SearchParameterConflictResolver(options, NullLogger<SearchParameterConflictResolver>.Instance);

        var usCoreParam = CreateSearchParameter("identifier", "http://hl7.org/fhir/us/core/SearchParameter/patient-identifier");
        var auBaseParam = CreateSearchParameter("identifier", "http://hl7.org/fhir/au/base/SearchParameter/patient-identifier");
        var hl7v2Param = CreateSearchParameter("identifier", "http://hl7.org/fhir/hl7v2/SearchParameter/patient-identifier");

        var metadata = new Dictionary<string, PackageMetadata>
        {
            [usCoreParam.Url.ToString()] = new PackageMetadata
            {
                PackageId = "hl7.fhir.us.core",
                PackageVersion = "5.0.1",
                LoadedDate = DateTimeOffset.UtcNow
            },
            [auBaseParam.Url.ToString()] = new PackageMetadata
            {
                PackageId = "hl7.fhir.au.base",
                PackageVersion = "4.1.0",
                LoadedDate = DateTimeOffset.UtcNow
            },
            [hl7v2Param.Url.ToString()] = new PackageMetadata
            {
                PackageId = "hl7.fhir.hl7v2",
                PackageVersion = "3.0.0",
                LoadedDate = DateTimeOffset.UtcNow
            }
        };

        // Act - Try different load orders
        var winner1 = resolver.ResolveConflict(
            new[] { usCoreParam, auBaseParam, hl7v2Param },
            "identifier",
            "Patient",
            metadata);

        var winner2 = resolver.ResolveConflict(
            new[] { hl7v2Param, auBaseParam, usCoreParam },
            "identifier",
            "Patient",
            metadata);

        var winner3 = resolver.ResolveConflict(
            new[] { auBaseParam, hl7v2Param, usCoreParam },
            "identifier",
            "Patient",
            metadata);

        // Assert - All should pick US Core (highest priority)
        Assert.Equal(usCoreParam.Url, winner1.Url);
        Assert.Equal(usCoreParam.Url, winner2.Url);
        Assert.Equal(usCoreParam.Url, winner3.Url);
    }

    #endregion

    #region Semantic Versioning Tests

    [Fact]
    public void GivenSemanticVersioning_WhenMultipleVersions_ThenNewestWins()
    {
        // Arrange
        var options = new SearchParameterResolutionOptions
        {
            PackagePriorityOrder = null,
            UseSemanticVersioning = true,
            LogConflicts = false
        };
        var resolver = new SearchParameterConflictResolver(options, NullLogger<SearchParameterConflictResolver>.Instance);

        var version501 = CreateSearchParameter("name", "http://hl7.org/fhir/us/core/SearchParameter/patient-name-501");
        var version610 = CreateSearchParameter("name", "http://hl7.org/fhir/us/core/SearchParameter/patient-name-610");
        var version420 = CreateSearchParameter("name", "http://hl7.org/fhir/us/core/SearchParameter/patient-name-420");

        var metadata = new Dictionary<string, PackageMetadata>
        {
            [version501.Url.ToString()] = new PackageMetadata
            {
                PackageId = "hl7.fhir.us.core",
                PackageVersion = "5.0.1",
                LoadedDate = DateTimeOffset.UtcNow
            },
            [version610.Url.ToString()] = new PackageMetadata
            {
                PackageId = "hl7.fhir.us.core",
                PackageVersion = "6.1.0",
                LoadedDate = DateTimeOffset.UtcNow
            },
            [version420.Url.ToString()] = new PackageMetadata
            {
                PackageId = "hl7.fhir.us.core",
                PackageVersion = "4.2.0",
                LoadedDate = DateTimeOffset.UtcNow
            }
        };

        // Act
        var winner = resolver.ResolveConflict(
            new[] { version501, version420, version610 },
            "name",
            "Patient",
            metadata);

        // Assert
        Assert.Equal(version610.Url, winner.Url);
    }

    [Fact]
    public void GivenSemanticVersioning_WhenLoadedMultipleTimes_ThenResultIsDeterministic()
    {
        // Arrange
        var options = new SearchParameterResolutionOptions
        {
            PackagePriorityOrder = null,
            UseSemanticVersioning = true,
            LogConflicts = false
        };
        var resolver = new SearchParameterConflictResolver(options, NullLogger<SearchParameterConflictResolver>.Instance);

        var param1 = CreateSearchParameter("code", "http://example.com/sp1");
        var param2 = CreateSearchParameter("code", "http://example.com/sp2");
        var param3 = CreateSearchParameter("code", "http://example.com/sp3");

        var metadata = new Dictionary<string, PackageMetadata>
        {
            [param1.Url.ToString()] = new PackageMetadata
            {
                PackageId = "pkg.a",
                PackageVersion = "2.0.0",
                LoadedDate = DateTimeOffset.UtcNow
            },
            [param2.Url.ToString()] = new PackageMetadata
            {
                PackageId = "pkg.b",
                PackageVersion = "3.0.0",
                LoadedDate = DateTimeOffset.UtcNow
            },
            [param3.Url.ToString()] = new PackageMetadata
            {
                PackageId = "pkg.c",
                PackageVersion = "1.0.0",
                LoadedDate = DateTimeOffset.UtcNow
            }
        };

        // Act - Load 100 times in different orders
        var results = new List<string>();

        // Test all 6 permutations of 3 items
        var permutations = new[]
        {
            new[] { param1, param2, param3 },
            new[] { param1, param3, param2 },
            new[] { param2, param1, param3 },
            new[] { param2, param3, param1 },
            new[] { param3, param1, param2 },
            new[] { param3, param2, param1 }
        };

        foreach (var candidates in permutations)
        {
            var winner = resolver.ResolveConflict(
                candidates,
                "code",
                "Observation",
                metadata);

            results.Add(winner.Url.ToString());
        }

        // Assert - All results should be identical
        Assert.True(results.All(r => r == results[0]), "Results should be deterministic");
        Assert.Equal(param2.Url.ToString(), results[0]); // pkg.b has highest version (3.0.0)
    }

    #endregion

    #region Per-Resource-Type Resolution Tests

    [Fact]
    public void GivenSameCodeOnDifferentResourceTypes_WhenResolvingConflicts_ThenResolvesIndependentlyPerResourceType()
    {
        // Arrange: This is a critical test demonstrating FHIR-compliant per-resource-type conflict resolution.
        // Example: US Core defines custom "identifier" for Patient with priority 1,
        // and AU Base defines custom "identifier" for Organization with priority 2.
        // These should NOT interfere with each other.

        var options = new SearchParameterResolutionOptions
        {
            PackagePriorityOrder = new List<string> { "hl7.fhir.us.core", "hl7.fhir.au.base" },
            LogConflicts = false
        };
        var resolver = new SearchParameterConflictResolver(options, NullLogger<SearchParameterConflictResolver>.Instance);

        // Base FHIR "identifier" parameter for both Patient and Organization
        var patientIdentifierBase = CreateSearchParameter("identifier", "http://hl7.org/fhir/SearchParameter/Patient-identifier", "Patient");
        var organizationIdentifierBase = CreateSearchParameter("identifier", "http://hl7.org/fhir/SearchParameter/Organization-identifier", "Organization");

        // US Core custom "identifier" for Patient only
        var patientIdentifierUSCore = CreateSearchParameter("identifier", "http://hl7.org/fhir/us/core/SearchParameter/patient-identifier", "Patient");

        // AU Base custom "identifier" for Organization only
        var organizationIdentifierAUBase = CreateSearchParameter("identifier", "http://hl7.org/fhir/au/base/SearchParameter/organization-identifier", "Organization");

        var metadata = new Dictionary<string, PackageMetadata>
        {
            [patientIdentifierBase.Url.ToString()] = new PackageMetadata { PackageId = "hl7.fhir.r4.core", PackageVersion = "4.0.1", LoadedDate = DateTimeOffset.UtcNow },
            [organizationIdentifierBase.Url.ToString()] = new PackageMetadata { PackageId = "hl7.fhir.r4.core", PackageVersion = "4.0.1", LoadedDate = DateTimeOffset.UtcNow },
            [patientIdentifierUSCore.Url.ToString()] = new PackageMetadata { PackageId = "hl7.fhir.us.core", PackageVersion = "5.0.1", LoadedDate = DateTimeOffset.UtcNow },
            [organizationIdentifierAUBase.Url.ToString()] = new PackageMetadata { PackageId = "hl7.fhir.au.base", PackageVersion = "4.1.0", LoadedDate = DateTimeOffset.UtcNow }
        };

        // Act: Resolve conflicts separately for Patient and Organization

        // For Patient: Should choose US Core (higher priority than base)
        var patientWinner = resolver.ResolveConflict(
            new[] { patientIdentifierBase, patientIdentifierUSCore },
            "identifier",
            "Patient",
            metadata);

        // For Organization: Should choose AU Base (only custom option for this resource type)
        var organizationWinner = resolver.ResolveConflict(
            new[] { organizationIdentifierBase, organizationIdentifierAUBase },
            "identifier",
            "Organization",
            metadata);

        // Assert: Winners should be independent
        Assert.Equal(patientIdentifierUSCore.Url, patientWinner.Url);
        Assert.Equal(organizationIdentifierAUBase.Url, organizationWinner.Url);

        // Verify they're different - the same code resolved to different parameters
        Assert.NotEqual(patientWinner.Url, organizationWinner.Url);
    }

    [Fact]
    public void GivenResourceTypeValidation_WhenCandidateDoesNotApplyToResourceType_ThenLogsErrorButContinues()
    {
        // Arrange: Test the validation logic that catches bugs in grouping
        var options = new SearchParameterResolutionOptions
        {
            PackagePriorityOrder = new List<string> { "pkg.a", "pkg.b" },
            LogConflicts = false
        };

        // Create a mock logger to capture error logs
        var mockLoggerFactory = new TestLoggerFactory();
        var resolver = new SearchParameterConflictResolver(options, mockLoggerFactory.CreateLogger<SearchParameterConflictResolver>());

        var paramForPatient = CreateSearchParameter("test", "http://example.com/sp1", "Patient");
        var paramForOrganization = CreateSearchParameter("test", "http://example.com/sp2", "Organization");

        var metadata = new Dictionary<string, PackageMetadata>
        {
            [paramForPatient.Url.ToString()] = new PackageMetadata { PackageId = "pkg.a", PackageVersion = "1.0.0", LoadedDate = DateTimeOffset.UtcNow },
            [paramForOrganization.Url.ToString()] = new PackageMetadata { PackageId = "pkg.b", PackageVersion = "1.0.0", LoadedDate = DateTimeOffset.UtcNow }
        };

        // Act: Try to resolve conflict with mismatched resource types
        // This simulates a bug where parameters for different resource types get grouped together
        var winner = resolver.ResolveConflict(
            new[] { paramForPatient, paramForOrganization },
            "test",
            "Patient",  // Only Patient applies to "Patient" resource type
            metadata);

        // Assert: Should still return a winner (pkg.a) but should have logged an error
        Assert.NotNull(winner);
        Assert.Equal(paramForPatient.Url, winner.Url);

        // Verify error was logged
        Assert.True(mockLoggerFactory.ErrorLogsContain("INTERNAL ERROR"),
            "Should log error when candidate doesn't apply to resource type");
    }

    #endregion

    #region Helper Methods

    private static readonly string[] PatientResourceType = new[] { "Patient" };
    private static readonly string[] OrganizationResourceType = new[] { "Organization" };

    private SearchParameterInfo CreateSearchParameter(string code, string url)
    {
        return CreateSearchParameter(code, url, "Patient");
    }

    private SearchParameterInfo CreateSearchParameter(string code, string url, string resourceType)
    {
        var resourceTypes = resourceType switch
        {
            "Patient" => PatientResourceType,
            "Organization" => OrganizationResourceType,
            _ => new[] { resourceType }
        };

        return new SearchParameterInfo(
            name: $"Test_{code}",
            code: code,
            searchParamType: SearchParamType.String,
            url: new Uri(url),
            components: null,
            expression: $"{resourceType}.{code}",
            targetResourceTypes: null,
            baseResourceTypes: resourceTypes,
            description: $"Test search parameter for {code}");
    }

    #endregion
}

/// <summary>
/// Test helper: Captures log messages for verification in tests.
/// </summary>
internal class TestLoggerFactory
{
    private readonly List<string> _errorLogs = new();

    public ILogger<T> CreateLogger<T>() where T : class
    {
        return new TestLogger<T>(_errorLogs);
    }

    public bool ErrorLogsContain(string text)
    {
        return _errorLogs.Any(log => log.Contains(text, StringComparison.OrdinalIgnoreCase));
    }

    private class TestLogger<T> : ILogger<T> where T : class
    {
        private readonly List<string> _logs;

        public TestLogger(List<string> logs)
        {
            _logs = logs;
        }

        IDisposable? ILogger.BeginScope<TState>(TState state) => null;

        bool ILogger.IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

        void ILogger.Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Error)
            {
                _logs.Add(formatter(state, exception));
            }
        }
    }
}
