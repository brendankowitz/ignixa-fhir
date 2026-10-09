using System.Text;
using System.Text.Json;
using Ignixa.Abstractions;
using Ignixa.Application.Features.Conformance;
using Ignixa.Application.Features.Resource;
using Ignixa.Application.Features.Search;
using Ignixa.Application.Infrastructure;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Exceptions;
using Ignixa.Domain.Models;
using Ignixa.Models;
using Ignixa.Search.Indexing;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Generated;
using Ignixa.Validation.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Tests.Features.Resource;

public class CreateOrUpdateResourceProvenanceBarrierTests
{
    [Fact]
    public async Task GivenProvenanceRemainsBehindTheBarrierAfterTheMainWrite_WhenHandlingTheRequest_ThenMainWriteSucceedsWithWarning()
    {
        var repository = Substitute.For<IFhirRepository>();
        var writeCount = 0;
        repository.CreateOrUpdateAsync(Arg.Any<ResourceWrapper>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var wrapper = call.Arg<ResourceWrapper>();
                writeCount++;
                if (writeCount == 1)
                {
                    return new ValueTask<UpdateResult>(new UpdateResult(
                        new ResourceKey("Patient", "p1", "1", 1),
                        Encoding.UTF8.GetBytes("""{"resourceType":"Patient","id":"p1"}"""),
                        DateTimeOffset.UtcNow)
                    {
                        IsCreated = true
                    });
                }

                return ValueTask.FromException<UpdateResult>(
                    new StaleConformanceDefinitionsException(100 + writeCount, wrapper.DefinitionsEventId, 29));
            });
        var repositories = Substitute.For<IFhirRepositoryFactory>();
        repositories.GetRepositoryAsync(1, Arg.Any<CancellationToken>()).Returns(repository);
        var partitions = Substitute.For<IPartitionStrategy>();
        partitions.DetermineWritePartition(
                Arg.Any<PartitionResolutionContext>(),
                Arg.Any<ResourceJsonNode>())
            .Returns(new RequestPartition { Mode = PartitionMode.Isolated, PartitionIds = [1] });
        var context = Substitute.For<IFhirRequestContext>();
        context.TenantId.Returns(1);
        context.FhirVersion.Returns(FhirVersion.R4);
        context.TenantConfiguration.Returns(
            new TenantConfiguration { TenantId = 1, DisplayName = "Provenance test", FhirVersion = "4.0" });
        var accessor = Substitute.For<IFhirRequestContextAccessor>();
        accessor.RequestContext.Returns(context);
        var indexer = Substitute.For<ISearchIndexer>();
        indexer.Extract(Arg.Any<IElement>()).Returns(Array.Empty<SearchIndexEntry>());
        var versions = Substitute.For<IFhirVersionContext>();
        versions.GetSchemaProvider(FhirVersion.R4, 1).Returns(new R4CoreSchemaProvider());
        versions.GetBaseSchemaProvider(FhirVersion.R4).Returns(new R4CoreSchemaProvider());
        versions.GetDefinitionsHandle(FhirVersion.R4, 1)
            .Returns(new DefinitionsHandle(indexer, new R4CoreSchemaProvider(), 11));
        var validationResolver = Substitute.For<IValidationSchemaResolver>();
        validationResolver.GetSchema("Provenance").Returns((ValidationSchema?)null);
        var synchronizer = Substitute.For<IConformanceDefinitionsSynchronizer>();
        var retryPolicy = new ConformanceBarrierRetryPolicy(
            synchronizer,
            TimeSpan.FromSeconds(17),
            NullLogger<ConformanceBarrierRetryPolicy>.Instance);
        var handler = new CreateOrUpdateResourceHandler(
            partitions,
            repositories,
            accessor,
            versions,
            _ => validationResolver,
            retryPolicy,
            NullLogger<CreateOrUpdateResourceHandler>.Instance);
        var command = new CreateOrUpdateResourceCommand(
            "Patient",
            "p1",
            ResourceJsonNode.Parse("""{"resourceType":"Patient","id":"p1"}"""),
            HttpMethod.Put,
            ProvenanceResource: new Provenance());

        var result = await handler.HandleAsync(command, CancellationToken.None);

        result.Key.ShouldBe(new ResourceKey("Patient", "p1", "1", 1));
        result.OperationOutcomeBytes.ShouldNotBeNull();
        using var warning = JsonDocument.Parse(result.OperationOutcomeBytes.Value);
        warning.RootElement.GetProperty("resourceType").GetString().ShouldBe("OperationOutcome");
        warning.RootElement.GetProperty("issue")[0].GetProperty("severity").GetString().ShouldBe("warning");
        writeCount.ShouldBe(3);
    }
}
