using Ignixa.Application.Features.Bundle;
using Ignixa.Domain.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using static Ignixa.Application.Tests.Features.TestWritePath;

namespace Ignixa.Application.Tests.Features.Bundle;

public class DeferredWriteCoordinatorResponseTests
{
    [Fact]
    public async Task GivenAResponseWithAnAliasedReference_WhenCompleted_ThenTheRewrittenJsonIsSerializedLikeEveryOtherEntry()
    {
        var coordinator = await DeferredWriteCoordinator.CreateAsync(
            1,
            Repositories(Substitute.For<IFhirRepository>()),
            IsolatedPartition(),
            RequestContext(),
            Versions(definitionsEventId: 1, Indexer()),
            TestConformanceBarrierRetryPolicy.Create(),
            NullLogger<DeferredWriteCoordinator>.Instance);
        var response = new BundleEntryResponse
        {
            StatusCode = 201,
            Status = "201 Created",
            ResourceJson = """{"resourceType":"Observation","meta":{"lastUpdated":"2020-01-01T00:00:00+00:00"},"subject":{"reference":"urn:uuid:p"},"note":[]}""",
        };

        var completed = coordinator.CompleteResponse(0, response, new Dictionary<string, string> { ["urn:uuid:p"] = "Patient/1" });

        completed.ResourceJson.ShouldBe(
            """{"resourceType":"Observation","meta":{"lastUpdated":"2020-01-01T00:00:00+00:00"},"subject":{"reference":"Patient/1"}}""");
    }
}
