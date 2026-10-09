using Ignixa.Abstractions;
using Ignixa.Application.Features.Conformance;
using Ignixa.Application.Features.Search;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Search.Definition;
using Ignixa.Search.Indexing;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Tests.Features.Search;

public class DefinitionsHandleTests
{
    [Fact]
    public void GivenPublishedTenantSnapshot_WhenFutureSnapshotBuilds_ThenItUsesADetachedSchemaProvider()
    {
        var packageRepository = Substitute.For<IPackageResourceRepository>();
        packageRepository.GetAllStructureDefinitionsAsync(
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<PackageResource>>([]));
        using var context = new FhirVersionContext(
            NullLoggerFactory.Instance,
            new SearchParameterResolutionOptions(),
            NullFhirBaseUriProvider.Instance,
            packageRepository,
            Substitute.For<IPackageResourceProvider>());
        using var state = new ConformanceState();
        var stateSnapshot = state.CreateSnapshot();
        var generationOne = context.CreateConformanceDefinitionsSnapshot(
            FhirVersion.R4,
            tenantId: 1,
            stateSnapshot,
            generation: 11);
        context.PublishConformanceDefinitionsSnapshot(FhirVersion.R4, tenantId: 1, generationOne);

        var generationTwo = context.CreateConformanceDefinitionsSnapshot(
            FhirVersion.R4,
            tenantId: 1,
            stateSnapshot,
            generation: 29);

        generationTwo.Handle.SchemaProvider.ShouldNotBeSameAs(generationOne.Handle.SchemaProvider);
        context.GetDefinitionsHandle(FhirVersion.R4, tenantId: 1)
            .SchemaProvider.ShouldBeSameAs(generationOne.Handle.SchemaProvider);
        context.GetSchemaProvider(FhirVersion.R4, tenantId: 1)
            .ShouldBeSameAs(generationOne.Handle.SchemaProvider);
    }

    [Fact]
    public void GivenAnOlderConsumerSnapshotCompletesAfterANewerSnapshot_WhenPublished_ThenAllConsumersRemainOnTheNewerGeneration()
    {
        var olderDefinitions = Substitute.For<ISearchParameterDefinitionManager>();
        var newerDefinitions = Substitute.For<ISearchParameterDefinitionManager>();
        var older = new ConformanceDefinitionsSnapshot(
            olderDefinitions,
            olderDefinitions,
            new DefinitionsHandle(
                Substitute.For<ISearchIndexer>(),
                Substitute.For<IFhirSchemaProvider>(),
                11));
        var newer = new ConformanceDefinitionsSnapshot(
            newerDefinitions,
            newerDefinitions,
            new DefinitionsHandle(
                Substitute.For<ISearchIndexer>(),
                Substitute.For<IFhirSchemaProvider>(),
                29));
        var slot = new ConformanceDefinitionsSnapshotSlot(older);

        slot.Publish(newer);
        slot.Publish(new ConformanceDefinitionsSnapshot(
            olderDefinitions,
            olderDefinitions,
            new DefinitionsHandle(
                Substitute.For<ISearchIndexer>(),
                Substitute.For<IFhirSchemaProvider>(),
                17)));

        slot.Current.Generation.ShouldBe(29);
        slot.Current.ExtractionDefinitions.ShouldBeSameAs(newerDefinitions);
        slot.Current.SearchableDefinitions.ShouldBeSameAs(newerDefinitions);
        slot.ExtractionDefinitions.ShouldNotBeSameAs(olderDefinitions);
        slot.SearchableDefinitions.ShouldNotBeSameAs(olderDefinitions);
    }

    [Fact]
    public void GivenSameEventIdRepositoryRebuild_WhenPublished_ThenPublicationSequenceBreaksTheTie()
    {
        var definitions = Substitute.For<ISearchParameterDefinitionManager>();
        var first = new ConformanceDefinitionsSnapshot(
            definitions,
            definitions,
            new DefinitionsHandle(
                Substitute.For<ISearchIndexer>(),
                Substitute.For<IFhirSchemaProvider>(),
                29),
            PublicationSequence: 3);
        var replacement = new ConformanceDefinitionsSnapshot(
            definitions,
            definitions,
            new DefinitionsHandle(
                Substitute.For<ISearchIndexer>(),
                Substitute.For<IFhirSchemaProvider>(),
                29),
            PublicationSequence: 4);
        var slot = new ConformanceDefinitionsSnapshotSlot(first);

        slot.Publish(replacement);

        slot.Current.ShouldBeSameAs(replacement);
    }
}
