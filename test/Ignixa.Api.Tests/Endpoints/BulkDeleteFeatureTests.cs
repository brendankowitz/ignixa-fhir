using Ignixa.Application.Features.BulkDelete;
using Shouldly;
using Xunit;

namespace Ignixa.Api.Tests.Endpoints;

public sealed class BulkDeleteFeatureTests
{
    [Fact]
    public void GivenBulkDeleteFeature_WhenInspected_ThenItDeclaresSystemAndAllResourcesOperations()
    {
        var feature = new BulkDeleteFeature();

        feature.SystemOperations.ShouldBe(["bulk-delete"]);
        feature.ResourceOperations.ShouldContainKey("*");
        feature.ResourceOperations["*"].ShouldBe(["bulk-delete"]);
        feature.SupportedFhirVersions.ShouldBeNull();
    }
}
