// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;
using Ignixa.Api.E2ETests._Infrastructure;
using Ignixa.Api.E2ETests._Infrastructure.Base;

namespace Ignixa.Api.E2ETests.Search.IncludeRevinclude;

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
        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.BadRequest);
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
        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// Tests that _revinclude:iterate without explicit target type uses the search parameter's target types.
    /// Note: Unlike the original Microsoft FHIR server implementation which threw BadRequest,
    /// our implementation allows unspecified target types and uses the search parameter's
    /// TargetResourceTypes to determine which resources to look for in the iteration batch.
    /// Original test: GivenARevIncludeIterateSearchExpressionWithMultipleResultsSetsWithoutSpecificRevIncludeIterateTargetType_WhenSearched_ShouldThrowBadRequestExceptionWithIssue
    /// </summary>
    [Fact]
    public async Task GivenARevIncludeIterateSearchExpressionWithoutSpecificTargetType_WhenSearched_ShouldSucceed()
    {
        // Act - _revinclude:iterate=Patient:general-practitioner without explicit target type
        // The general-practitioner search parameter targets: Organization | Practitioner | PractitionerRole
        // Our implementation will look for any of these types in the iteration batch
        var response = await Client.GetAsync(
            "/MedicationDispense?_include=MedicationDispense:performer:Practitioner&_include=MedicationDispense:prescription&_include:iterate=MedicationRequest:requester:Practitioner&_revinclude:iterate=Patient:general-practitioner");

        // Assert - should succeed (not BadRequest) as we now support unspecified target types
        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
    }
}
