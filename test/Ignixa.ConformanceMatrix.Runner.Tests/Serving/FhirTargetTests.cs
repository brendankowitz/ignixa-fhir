using System.Net;
using Ignixa.ConformanceMatrix.Runner.Serving;
using Shouldly;

namespace Ignixa.ConformanceMatrix.Runner.Tests.Serving;

public class FhirTargetTests
{
    private const string CapabilityStatementJson = """
        {
          "resourceType": "CapabilityStatement",
          "status": "active",
          "fhirVersion": "4.0.1",
          "rest": [ { "mode": "server" } ]
        }
        """;

    [Fact]
    public async Task GivenCapabilityStatementAvailable_WhenRequestedTwice_ThenOneFetchYieldsDistinctInstances()
    {
        // Arrange
        var metadataCalls = 0;
        using var handler = new StubHttpMessageHandler(_ =>
        {
            metadataCalls++;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(CapabilityStatementJson)
            };
        });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://fhir.test/") };
        using var target = new FhirTarget(httpClient);

        // Act
        var first = await target.GetCapabilityStatementAsync(CancellationToken.None);
        var second = await target.GetCapabilityStatementAsync(CancellationToken.None);

        // Assert — one /metadata round-trip, but each run gets its own node so the unsynchronized
        // ToElement/source-node memoization is never shared across concurrent evaluations.
        metadataCalls.ShouldBe(1);
        first.ShouldNotBeNull();
        second.ShouldNotBeNull();
        second.ShouldNotBeSameAs(first);
    }

    [Fact]
    public async Task GivenMetadataFailsOnFirstCall_WhenRequestedAgain_ThenFetchIsRetried()
    {
        // Arrange — first /metadata answers 503 (server still warming up), then recovers.
        var metadataCalls = 0;
        using var handler = new StubHttpMessageHandler(_ =>
        {
            metadataCalls++;
            return metadataCalls == 1
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(CapabilityStatementJson) };
        });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://fhir.test/") };
        using var target = new FhirTarget(httpClient);

        // Act
        var whileWarmingUp = await target.GetCapabilityStatementAsync(CancellationToken.None);
        var afterRecovery = await target.GetCapabilityStatementAsync(CancellationToken.None);
        var cached = await target.GetCapabilityStatementAsync(CancellationToken.None);

        // Assert — the failure is not cached for the process lifetime: gating fails open only for
        // the runs before the server recovered.
        whileWarmingUp.ShouldBeNull();
        afterRecovery.ShouldNotBeNull();
        cached.ShouldNotBeNull();
        metadataCalls.ShouldBe(2);
    }

    [Fact]
    public async Task GivenUnparseableMetadataBody_WhenRequested_ThenReturnsNullAndRetriesNextCall()
    {
        // Arrange
        var metadataCalls = 0;
        using var handler = new StubHttpMessageHandler(_ =>
        {
            metadataCalls++;
            return metadataCalls == 1
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>gateway error</html>") }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(CapabilityStatementJson) };
        });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://fhir.test/") };
        using var target = new FhirTarget(httpClient);

        // Act
        var fromBadBody = await target.GetCapabilityStatementAsync(CancellationToken.None);
        var fromGoodBody = await target.GetCapabilityStatementAsync(CancellationToken.None);

        // Assert
        fromBadBody.ShouldBeNull();
        fromGoodBody.ShouldNotBeNull();
        metadataCalls.ShouldBe(2);
    }
}
