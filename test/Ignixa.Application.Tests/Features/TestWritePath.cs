using Ignixa.Abstractions;
using Ignixa.Application.Features.Search;
using Ignixa.Application.Infrastructure;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Search.Indexing;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Generated;
using NSubstitute;

namespace Ignixa.Application.Tests.Features;

/// <summary>Substituted write-path dependencies for one isolated R4 tenant, id 1.</summary>
internal static class TestWritePath
{
    public static IFhirRepositoryFactory Repositories(IFhirRepository repository)
    {
        var repositories = Substitute.For<IFhirRepositoryFactory>();
        repositories.GetRepositoryAsync(1, Arg.Any<CancellationToken>()).Returns(repository);
        return repositories;
    }

    public static IPartitionStrategy IsolatedPartition()
    {
        var partitions = Substitute.For<IPartitionStrategy>();
        partitions.DetermineWritePartition(Arg.Any<PartitionResolutionContext>(), Arg.Any<ResourceJsonNode>())
            .Returns(new RequestPartition { Mode = PartitionMode.Isolated, PartitionIds = [1] });
        return partitions;
    }

    public static IFhirRequestContextAccessor RequestContext()
    {
        var context = Substitute.For<IFhirRequestContext>();
        context.TenantId.Returns(1);
        context.FhirVersion.Returns(FhirVersion.R4);
        context.TenantConfiguration.Returns(new TenantConfiguration { TenantId = 1, DisplayName = "Write path", FhirVersion = "4.0" });
        var accessor = Substitute.For<IFhirRequestContextAccessor>();
        accessor.RequestContext.Returns(context);
        return accessor;
    }

    /// <summary>Definitions published at <paramref name="definitionsEventId"/>, extracting nothing by default.</summary>
    public static IFhirVersionContext Versions(long definitionsEventId, ISearchIndexer? indexer = null)
    {
        if (indexer is null)
        {
            indexer = Substitute.For<ISearchIndexer>();
            indexer.Extract(Arg.Any<IElement>()).Returns(Array.Empty<SearchIndexEntry>());
        }

        var versions = Substitute.For<IFhirVersionContext>();
        versions.GetSchemaProvider(FhirVersion.R4, 1).Returns(new R4CoreSchemaProvider());
        versions.GetBaseSchemaProvider(FhirVersion.R4).Returns(new R4CoreSchemaProvider());
        versions.GetDefinitionsHandle(FhirVersion.R4, 1).Returns(new DefinitionsHandle(indexer, new R4CoreSchemaProvider(), definitionsEventId));
        return versions;
    }
}
