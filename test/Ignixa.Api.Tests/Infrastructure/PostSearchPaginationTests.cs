// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using FluentAssertions;
using Ignixa.Search.Parsing;
using Xunit;

namespace Ignixa.Api.Tests.Infrastructure;

/// <summary>
/// Unit tests for POST _search pagination link generation.
/// Verifies that pagination links preserve search parameters per FHIR specification.
///
/// FHIR Spec Requirement (https://build.fhir.org/search.html):
/// "All relevant paging links SHALL be expressed as GET requests"
/// "servers SHALL return the parameters that were actually used to process a search"
/// </summary>
public class PostSearchPaginationTests
{
    /// <summary>
    /// Tests that QueryString is built correctly from QueryParameter list.
    /// This is a behavior-verification test using reflection to access the private method.
    /// </summary>
    [Fact]
    public void GivenQueryParameters_WhenBuildingQueryString_ThenParametersArePreserved()
    {
        // Arrange
        var parameters = new List<QueryParameter>
        {
            new("name", "John"),
            new("birthdate", "gt2000-01-01"),
            new("_count", "20"),
        };

        // Act
        string queryString = BuildQueryStringFromParametersViaReflection(parameters);

        // Assert
        // Should contain all parameters except continuation token
        queryString.Should().Contain("name=John");
        queryString.Should().Contain("birthdate=gt2000-01-01");
        queryString.Should().Contain("_count=20");
        queryString.Should().StartWith("?");
    }

    [Fact]
    public void GivenParametersWithContinuationToken_WhenBuildingQueryString_ThenTokenIsExcluded()
    {
        // Arrange - continuation token should be excluded (added separately by serializer as 'after')
        var parameters = new List<QueryParameter>
        {
            new("name", "John"),
            new("ct", "abc123"),  // Legacy continuation token
            new("_count", "20"),
        };

        // Act
        string queryString = BuildQueryStringFromParametersViaReflection(parameters);

        // Assert
        queryString.Should().Contain("name=John");
        queryString.Should().Contain("_count=20");
        queryString.Should().NotContain("ct=");
    }

    [Fact]
    public void GivenParametersWithAfterToken_WhenBuildingQueryString_ThenTokenIsExcluded()
    {
        // Arrange - 'after' parameter should be excluded (it will be added by serializer)
        var parameters = new List<QueryParameter>
        {
            new("name", "John"),
            new("after", "xyz789"),  // Modern continuation token
        };

        // Act
        string queryString = BuildQueryStringFromParametersViaReflection(parameters);

        // Assert
        queryString.Should().Contain("name=John");
        queryString.Should().NotContain("after=");
    }

    [Fact]
    public void GivenEmptyParameterList_WhenBuildingQueryString_ThenReturnsEmptyString()
    {
        // Arrange
        var parameters = new List<QueryParameter>();

        // Act
        string queryString = BuildQueryStringFromParametersViaReflection(parameters);

        // Assert
        queryString.Should().Be(string.Empty);
    }

    [Fact]
    public void GivenNullParameterList_WhenBuildingQueryString_ThenReturnsEmptyString()
    {
        // Arrange
        IReadOnlyList<QueryParameter>? parameters = null;

        // Act
        string queryString = BuildQueryStringFromParametersViaReflection(parameters);

        // Assert
        queryString.Should().Be(string.Empty);
    }

    [Fact]
    public void GivenParametersWithSpecialCharacters_WhenBuildingQueryString_ThenValuesAreUrlEncoded()
    {
        // Arrange
        var parameters = new List<QueryParameter>
        {
            new("name", "John & Jane"),
            new("address", "123 Main St, Suite #100"),
        };

        // Act
        string queryString = BuildQueryStringFromParametersViaReflection(parameters);

        // Assert
        // Special characters should be URL-encoded
        queryString.Should().Contain("John%20%26%20Jane");  // & → %26, space → %20
        queryString.Should().Contain("123%20Main%20St%2C%20Suite%20%23100");  // comma → %2C, # → %23
        queryString.Should().StartWith("?");
    }

    [Fact]
    public void GivenMultipleParametersWithSameNameAsInclude_WhenBuildingQueryString_ThenAllIncludedOnce()
    {
        // Arrange - Multiple _include parameters can exist
        var parameters = new List<QueryParameter>
        {
            new("name", "John"),
            new("_include", "Patient:general-practitioner"),
            new("_include", "Patient:organization"),
        };

        // Act
        string queryString = BuildQueryStringFromParametersViaReflection(parameters);

        // Assert
        queryString.Should().Contain("name=John");
        // Both include parameters should be present
        var parts = queryString.Split("_include=");
        (parts.Length - 1).Should().Be(2);
    }

    // ========== Helper Methods ==========

    /// <summary>
    /// Uses reflection to invoke the private BuildQueryStringFromParameters method
    /// from FhirEndpoints class since we need to test it without making it public.
    /// </summary>
    private static string BuildQueryStringFromParametersViaReflection(IReadOnlyList<QueryParameter>? parameters)
    {
        var fhirEndpointsType = typeof(Ignixa.Api.Infrastructure.FhirEndpoints);
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;

        var method = fhirEndpointsType.GetMethod(
            "BuildQueryStringFromParameters",
            flags);

        if (method == null)
        {
            throw new InvalidOperationException(
                "BuildQueryStringFromParameters method not found. Method may have been removed or renamed.");
        }

        var result = method.Invoke(null, new object?[] { parameters });
        return (string)(result ?? string.Empty);
    }
}
