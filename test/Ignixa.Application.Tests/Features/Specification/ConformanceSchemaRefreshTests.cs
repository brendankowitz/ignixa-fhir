using Ignixa.Abstractions;
using Ignixa.Application.Features.Conformance;
using Ignixa.Application.Features.Search;
using Ignixa.Application.Features.Specification;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Search.Definition;
using Ignixa.Specification.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using PackageResourceProvider = Ignixa.PackageManagement.Infrastructure.PackageResourceProvider;

namespace Ignixa.Application.Tests.Features.Specification;

public class ConformanceSchemaRefreshTests
{
    [Fact]
    public async Task GivenPublishedSnapshotWithWarmPackageModel_WhenPackageInvalidates_ThenModelIsImmediatelyRemoved()
    {
        var repository = Substitute.For<IPackageResourceRepository>();
        repository.GetAllStructureDefinitionsAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PackageResource>)[new PackageResource
            {
                PackageId = "local.ignixa.sqlonfhir", PackageVersion = "2.1.0", ResourceType = "StructureDefinition",
                ResourceId = "ViewDefinition", FhirVersion = "4.0.1",
                Canonical = "https://sql-on-fhir.org/ig/StructureDefinition/ViewDefinition",
                ResourceJson = ViewDefinitionAdapterConversionTests.LoadEmbeddedDefinition()
            }]);
        using var registry = new CompositeSchemaProviderRegistry(
            NullLogger<CompositeSchemaProviderRegistry>.Instance, TimeSpan.FromMinutes(1));
        using var context = new FhirVersionContext(
            NullLoggerFactory.Instance,
            new SearchParameterResolutionOptions(),
            NullFhirBaseUriProvider.Instance,
            repository,
            new PackageResourceProvider(NullLogger<PackageResourceProvider>.Instance),
            registry);
        using var state = new ConformanceState();
        var snapshot = context.CreateConformanceDefinitionsSnapshot(
            FhirVersion.R4,
            tenantId: 1,
            state.CreateSnapshot(),
            generation: 11);
        context.PublishConformanceDefinitionsSnapshot(FhirVersion.R4, tenantId: 1, snapshot);
        snapshot.Handle.SchemaProvider.ResourceTypeNames.ShouldContain("ViewDefinition");
        repository.GetAllStructureDefinitionsAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PackageResource>)[]);

        await registry.InvalidateCacheForPackageAsync(
            "local.ignixa.sqlonfhir",
            tenantId: 1,
            CancellationToken.None);

        snapshot.Handle.SchemaProvider.ResourceTypeNames.ShouldNotContain("ViewDefinition");
    }

    [Fact]
    public async Task GivenWarmMissingModel_WhenImmediateTenantRefreshCompletes_ThenConsumersSeeTheNewSchemaWithoutDebounce()
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

        await registry.InvalidateCachesForTenantImmediatelyAsync(1, CancellationToken.None);

        provider.ResourceTypeNames.ShouldContain("ViewDefinition");
    }
}
