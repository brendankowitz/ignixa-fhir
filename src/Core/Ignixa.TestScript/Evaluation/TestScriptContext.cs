using System.Collections.Immutable;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Ignixa.TestScript.Client;
using Ignixa.TestScript.Reporting;

namespace Ignixa.TestScript.Evaluation;

public sealed record TestScriptContext
{
    public TestResponse? LastResponse { get; init; }
    public TestRequest? LastRequest { get; init; }
    public ImmutableDictionary<string, string> Variables { get; init; } =
        ImmutableDictionary<string, string>.Empty;
    public ImmutableDictionary<string, ResourceJsonNode> Fixtures { get; init; } =
        ImmutableDictionary<string, ResourceJsonNode>.Empty;
    public ImmutableDictionary<string, TestResponse> ResponseHistory { get; init; } =
        ImmutableDictionary<string, TestResponse>.Empty;
    public ImmutableDictionary<string, TestRequest> RequestHistory { get; init; } =
        ImmutableDictionary<string, TestRequest>.Empty;

    // Recorder is intentionally shared by reference across all derived contexts — every
    // with-expression copy keeps the same recorder instance, so all phases of a single test
    // execution write to one recorder. Mutating it from any derived context is observable everywhere.
    internal ITestScriptResultRecorder Recorder { get; init; } = new TestScriptResultRecorder();

    public TestScriptContext WithResponse(string? responseId, TestResponse response)
    {
        var stored = DeepCloneResponse(response);
        var ctx = this with { LastResponse = stored };
        if (responseId is not null)
            ctx = ctx with { ResponseHistory = ResponseHistory.SetItem(responseId, stored) };
        return ctx;
    }

    public TestScriptContext WithRequest(string? requestId, TestRequest request)
    {
        var stored = request.Body is not null
            ? request with { Body = DeepCloneResource(request.Body) }
            : request;
        var ctx = this with { LastRequest = stored };
        if (requestId is not null)
            ctx = ctx with { RequestHistory = RequestHistory.SetItem(requestId, stored) };
        return ctx;
    }

    public TestScriptContext WithVariable(string name, string value) =>
        this with { Variables = Variables.SetItem(name, value) };

    public TestScriptContext WithFixture(string id, ResourceJsonNode resource) =>
        this with { Fixtures = Fixtures.SetItem(id, DeepCloneResource(resource)) };

    private static ResourceJsonNode DeepCloneResource(ResourceJsonNode resource) =>
        JsonSourceNodeFactory.Parse(resource.MutableNode.DeepClone());

    private static TestResponse DeepCloneResponse(TestResponse response) =>
        response.Body is null
            ? response
            : response with { Body = DeepCloneResource(response.Body) };
}
