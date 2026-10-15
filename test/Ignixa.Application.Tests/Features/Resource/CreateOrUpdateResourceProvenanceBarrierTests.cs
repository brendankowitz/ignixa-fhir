using System.Text;
using System.Text.Json;
using Ignixa.Abstractions;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Exceptions;
using Ignixa.Domain.Models;
using Ignixa.Application.Features.Resource;
using Ignixa.Models;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Validation.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Tests.Features.Resource;

/// <summary>F4: the X-Provenance write is fenced by the conformance barrier like the main write.</summary>
public class CreateOrUpdateResourceProvenanceBarrierTests
{
    private readonly List<ResourceWrapper> _writes = [];

    [Fact]
    public async Task GivenProvenanceRemainsBehindTheBarrierAfterTheMainWrite_WhenHandlingTheRequest_ThenMainWriteSucceedsWithWarning()
    {
        var result = await HandleAsync(write => _writes.Count == 1
            ? Created(write)
            : ValueTask.FromException<UpdateResult>(new StaleConformanceDefinitionsException(100 + _writes.Count, write.DefinitionsEventId, 29)));

        result.Key.ShouldBe(new ResourceKey("Patient", "p1", "1", 1));
        result.OperationOutcomeBytes.ShouldNotBeNull();
        using var warning = JsonDocument.Parse(result.OperationOutcomeBytes.Value);
        warning.RootElement.GetProperty("resourceType").GetString().ShouldBe("OperationOutcome");
        warning.RootElement.GetProperty("issue")[0].GetProperty("severity").GetString().ShouldBe("warning");
        _writes.Count.ShouldBe(3);
    }

    [Fact]
    public async Task GivenTheProvenanceWriteSucceeds_WhenHandlingTheRequest_ThenItCarriesTheDefinitionsEventIdAndNoWarning()
    {
        var result = await HandleAsync(Created);

        result.OperationOutcomeBytes.ShouldBeNull();
        _writes.Select(write => (write.ResourceType, write.DefinitionsEventId)).ShouldBe([("Patient", 11L), ("Provenance", 11L)]);
    }

    // PUT Patient/p1 with an X-Provenance header, against definitions published at event 11.
    private Task<UpdateResult> HandleAsync(Func<ResourceWrapper, ValueTask<UpdateResult>> write)
    {
        var repository = Substitute.For<IFhirRepository>();
        repository.CreateOrUpdateAsync(Arg.Any<ResourceWrapper>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                _writes.Add(call.Arg<ResourceWrapper>());
                return write(call.Arg<ResourceWrapper>());
            });
        var validation = Substitute.For<IValidationSchemaResolver>();
        validation.GetSchema("Provenance").Returns((ValidationSchema?)null);
        var handler = new CreateOrUpdateResourceHandler(
            TestWritePath.IsolatedPartition(),
            TestWritePath.Repositories(repository),
            TestWritePath.RequestContext(),
            TestWritePath.Versions(definitionsEventId: 11),
            _ => validation,
            TestConformanceBarrierRetryPolicy.Create(),
            NullLogger<CreateOrUpdateResourceHandler>.Instance);
        return handler.HandleAsync(
            new CreateOrUpdateResourceCommand(
                "Patient", "p1", ResourceJsonNode.Parse("""{"resourceType":"Patient","id":"p1"}"""), HttpMethod.Put, ProvenanceResource: new Provenance()),
            CancellationToken.None);
    }

    private static ValueTask<UpdateResult> Created(ResourceWrapper write) =>
        new(new UpdateResult(
            new ResourceKey(write.ResourceType, write.ResourceId, "1", 1),
            Encoding.UTF8.GetBytes($$"""{"resourceType":"{{write.ResourceType}}","id":"{{write.ResourceId}}"}"""),
            DateTimeOffset.UtcNow) { IsCreated = true });
}
