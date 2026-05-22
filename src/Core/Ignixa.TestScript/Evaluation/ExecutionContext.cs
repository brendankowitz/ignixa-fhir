using System.Collections.Immutable;
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

    // Recorder is intentionally shared across all derived contexts — all phases of a single
    // test execution write to the same recorder instance via with-expression copies.
    internal ITestScriptResultRecorder Recorder { get; init; } = new TestScriptResultRecorder();

    public TestScriptContext WithResponse(string? responseId, TestResponse response)
    {
        var ctx = this with { LastResponse = response };
        if (responseId is not null)
            ctx = ctx with { ResponseHistory = ResponseHistory.SetItem(responseId, response) };
        return ctx;
    }

    public TestScriptContext WithRequest(string? requestId, TestRequest request)
    {
        var ctx = this with { LastRequest = request };
        if (requestId is not null)
            ctx = ctx with { RequestHistory = RequestHistory.SetItem(requestId, request) };
        return ctx;
    }

    public TestScriptContext WithVariable(string name, string value) =>
        this with { Variables = Variables.SetItem(name, value) };

    public TestScriptContext WithFixture(string id, ResourceJsonNode resource) =>
        this with { Fixtures = Fixtures.SetItem(id, resource) };
}
