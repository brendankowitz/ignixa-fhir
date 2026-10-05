using Ignixa.Abstractions;
using Ignixa.Application.Features.Specification;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Specification.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using PackageResourceProvider = Ignixa.PackageManagement.Infrastructure.PackageResourceProvider;

namespace Ignixa.Application.Tests.Features.Specification;

public class PackageSchemaInvalidationTests
{
    [Fact]
    public async Task GivenWarmedMissingModel_WhenPackageInvalidationCompletes_ThenNewTypeIsImmediatelyVisible()
    {
        var repository = Substitute.For<IPackageResourceRepository>();
        repository.GetAllStructureDefinitionsAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PackageResource>)[]);
        var provider = new CompositeStructureDefinitionSummaryProvider(
            FhirVersion.R4.GetSchemaProvider(), repository,
            new PackageResourceProvider(NullLogger<PackageResourceProvider>.Instance), "4.0.1",
            NullLogger<CompositeStructureDefinitionSummaryProvider>.Instance);
        using var registry = new CompositeSchemaProviderRegistry(
            NullLogger<CompositeSchemaProviderRegistry>.Instance, TimeSpan.FromMinutes(1));
        registry.RegisterProvider(1, provider);
        provider.ResourceTypeNames.ShouldNotContain("ViewDefinition");
        repository.GetAllStructureDefinitionsAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PackageResource>)[new PackageResource
            {
                PackageId = "local.ignixa.sqlonfhir", PackageVersion = "2.1.0", ResourceType = "StructureDefinition",
                ResourceId = "ViewDefinition", FhirVersion = "4.0.1",
                Canonical = "https://sql-on-fhir.org/ig/StructureDefinition/ViewDefinition",
                ResourceJson = ViewDefinitionAdapterConversionTests.LoadEmbeddedDefinition()
            }]);

        await registry.InvalidateCacheForPackageAsync("local.ignixa.sqlonfhir", 1, CancellationToken.None);

        provider.ResourceTypeNames.ShouldContain("ViewDefinition");
    }
}
