// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using FluentAssertions;
using Ignixa.Api.E2ETests.Fixtures;

namespace Ignixa.Api.E2ETests.IncludeTests;

/// <summary>
/// E2E tests for FHIR _include and _revinclude error handling.
/// Tests validation and error scenarios.
/// Ported from: Microsoft.Health.Fhir.Tests.E2E.Rest.Search.IncludeSearchTests
/// </summary>
public class IncludeSearchTests_ErrorHandling : IncludeTestBase
{
    public IncludeSearchTests_ErrorHandling(IgnixaApiFixture fixture) : base(fixture)
    {
    }

    /// <summary>
    /// Tests that invalid target resource type returns error.
    /// Ported from: GivenAIncludeOrRevIncludeIterateSearchExpressionWithInvalidTargetResourceType_WhenSearched_ShouldThrowResourceNotSupportedException
    /// </summary>
    [Theory]
    [InlineData("_include")]
    [InlineData("_revinclude")]
    public async Task GivenAnIncludeOrRevIncludeWithInvalidTargetResourceType_WhenSearched_ShouldReturnBadRequest(string include)
    {
        // Act
        var response = await Client.GetAsync($"/Patient?{include}=Observation:subject:NotAResourceType");

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// Tests that empty target resource type returns error.
    /// Ported from: GivenAIncludeOrRevIncludeIterateSearchExpressionWithEmptyOrWhiteSpaceTargetResourceType_WhenSearched_ShouldThrowBadRequestException
    /// </summary>
    [Theory]
    [InlineData("_include", "")]
    [InlineData("_revinclude", "")]
    public async Task GivenAnIncludeOrRevIncludeWithEmptyTargetResourceType_WhenSearched_ShouldReturnBadRequest(string include, string target)
    {
        // Act
        var response = await Client.GetAsync($"/Patient?{include}=Observation:subject:{target}");

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// Tests that _revinclude:iterate without target type returns error.
    /// Ported from: GivenARevIncludeIterateSearchExpressionWithMultipleResultsSetsWithoutSpecificRevIncludeIterateTargetType_WhenSearched_ShouldThrowBadRequestExceptionWithIssue
    /// </summary>
    [Fact(Skip = "Waiting for _revinclude:iterate support")]
    public async Task GivenARevIncludeIterateSearchExpressionWithoutSpecificTargetType_WhenSearched_ShouldReturnBadRequest()
    {
        // Act
        var response = await Client.GetAsync(
            "/MedicationDispense?_include=MedicationDispense:performer:Practitioner&_include=MedicationDispense:prescription&_include:iterate=MedicationRequest:requester:Practitioner&_revinclude:iterate=Patient:general-practitioner");

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
    }
}
