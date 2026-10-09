using Ignixa.Api.Endpoints;
using Shouldly;
using Xunit;

namespace Ignixa.Api.Tests.Endpoints;

/// <summary>
/// <see cref="BulkDeleteEndpoints.BuildContentLocation"/> is a pure function extracted specifically so the
/// tenant-segment logic can be asserted without a TestServer/WebApplicationFactory: it must mirror the
/// route form the request actually used, not <c>IFhirRequestContext.BaseUri</c>'s single-tenant canonical
/// base (which collapses to the deployment root even for a tenant-explicit request).
/// </summary>
public sealed class BulkDeleteContentLocationTests
{
    [Fact]
    public void GivenTenantScopedRoute_WhenBuildingContentLocation_ThenTheTenantSegmentIsIncluded()
    {
        var location = BulkDeleteEndpoints.BuildContentLocation(
            "https", "fhir.example.org", pathBase: "", isTenantScopedRoute: true, tenantId: 1, jobId: "job-1");

        location.ShouldBe("https://fhir.example.org/tenant/1/_operations/bulk-delete/job-1");
    }

    [Fact]
    public void GivenAgnosticRoute_WhenBuildingContentLocation_ThenNoTenantSegmentIsIncluded()
    {
        var location = BulkDeleteEndpoints.BuildContentLocation(
            "https", "fhir.example.org", pathBase: "", isTenantScopedRoute: false, tenantId: 1, jobId: "job-1");

        location.ShouldBe("https://fhir.example.org/_operations/bulk-delete/job-1");
    }

    [Fact]
    public void GivenConfiguredPathBase_WhenBuildingContentLocationForATenantScopedRoute_ThenPathBasePrecedesTheTenantSegment()
    {
        var location = BulkDeleteEndpoints.BuildContentLocation(
            "https", "fhir.example.org", pathBase: "/fhir", isTenantScopedRoute: true, tenantId: 42, jobId: "job-2");

        location.ShouldBe("https://fhir.example.org/fhir/tenant/42/_operations/bulk-delete/job-2");
    }

    [Fact]
    public void GivenConfiguredPathBase_WhenBuildingContentLocationForAnAgnosticRoute_ThenPathBaseIsPreserved()
    {
        var location = BulkDeleteEndpoints.BuildContentLocation(
            "https", "fhir.example.org", pathBase: "/fhir", isTenantScopedRoute: false, tenantId: 42, jobId: "job-2");

        location.ShouldBe("https://fhir.example.org/fhir/_operations/bulk-delete/job-2");
    }
}
