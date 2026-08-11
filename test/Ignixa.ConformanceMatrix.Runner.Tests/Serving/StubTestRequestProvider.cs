using Ignixa.TestScript.Client;

namespace Ignixa.ConformanceMatrix.Runner.Tests.Serving;

/// <summary>An <see cref="ITestRequestProvider"/> that always returns the same canned response, so /run tests never hit a real FHIR server.</summary>
internal sealed class StubTestRequestProvider(TestResponse response) : ITestRequestProvider
{
    public Task<TestResponse> ExecuteAsync(TestRequest request, CancellationToken cancellationToken) => Task.FromResult(response);
}
