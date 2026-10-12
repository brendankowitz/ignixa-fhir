using System.Text;
using System.Text.Json;
using Ignixa.Abstractions;
using Ignixa.Application.Features.Resource;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Exceptions;
using Ignixa.Domain.Models;
using Ignixa.Models;
using Ignixa.Serialization.SourceNodes;
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
        var validationResolver = Substitute.For<IValidationSchemaResolver>();
        validationResolver.GetSchema("Provenance").Returns((ValidationSchema?)null);
        var handler = new CreateOrUpdateResourceHandler(
            TestWritePath.IsolatedPartition(),
            TestWritePath.Repositories(repository),
            TestWritePath.RequestContext(),
            TestWritePath.Versions(definitionsEventId: 11),
            _ => validationResolver,
            TestConformanceBarrierRetryPolicy.Create(),
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

    // F4: the Provenance write is fenced by the barrier like the main write, so it carries its handle's event id.
    [Fact]
    public async Task GivenTheProvenanceWriteSucceeds_WhenHandlingTheRequest_ThenItCarriesTheDefinitionsEventIdAndNoWarning()
    {
        var repository = Substitute.For<IFhirRepository>();
        var writes = new List<ResourceWrapper>();
        repository.CreateOrUpdateAsync(Arg.Any<ResourceWrapper>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var wrapper = call.Arg<ResourceWrapper>();
                writes.Add(wrapper);
                return new ValueTask<UpdateResult>(new UpdateResult(
                    new ResourceKey(wrapper.ResourceType, wrapper.ResourceId, "1", 1),
                    Encoding.UTF8.GetBytes($$"""{"resourceType":"{{wrapper.ResourceType}}","id":"{{wrapper.ResourceId}}"}"""),
                    DateTimeOffset.UtcNow)
                {
                    IsCreated = true
                });
            });
        var validationResolver = Substitute.For<IValidationSchemaResolver>();
        validationResolver.GetSchema("Provenance").Returns((ValidationSchema?)null);
        var handler = new CreateOrUpdateResourceHandler(
            TestWritePath.IsolatedPartition(),
            TestWritePath.Repositories(repository),
            TestWritePath.RequestContext(),
            TestWritePath.Versions(definitionsEventId: 11),
            _ => validationResolver,
            TestConformanceBarrierRetryPolicy.Create(),
            NullLogger<CreateOrUpdateResourceHandler>.Instance);
        var command = new CreateOrUpdateResourceCommand(
            "Patient",
            "p1",
            ResourceJsonNode.Parse("""{"resourceType":"Patient","id":"p1"}"""),
            HttpMethod.Put,
            ProvenanceResource: new Provenance());

        var result = await handler.HandleAsync(command, CancellationToken.None);

        result.OperationOutcomeBytes.ShouldBeNull();
        writes.Select(write => (write.ResourceType, write.DefinitionsEventId)).ShouldBe([("Patient", 11L), ("Provenance", 11L)]);
    }
}
