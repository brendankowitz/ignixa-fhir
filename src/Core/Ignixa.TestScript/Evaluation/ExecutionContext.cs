using System.Collections.Immutable;
using System.Text.Json.Nodes;
using Ignixa.TestScript.Client;
using Ignixa.TestScript.Reporting;

namespace Ignixa.TestScript.Evaluation;

public sealed record TestScriptContext
{
    public required IFhirClientRegistry ClientRegistry { get; init; }
    public FhirResponse? LastResponse { get; init; }
    public FhirRequest? LastRequest { get; init; }
    public ImmutableDictionary<string, string> Variables { get; init; } =
        ImmutableDictionary<string, string>.Empty;
    public ImmutableDictionary<string, JsonNode> Fixtures { get; init; } =
        ImmutableDictionary<string, JsonNode>.Empty;
    public ImmutableDictionary<string, FhirResponse> ResponseHistory { get; init; } =
        ImmutableDictionary<string, FhirResponse>.Empty;
    public ImmutableDictionary<string, FhirRequest> RequestHistory { get; init; } =
        ImmutableDictionary<string, FhirRequest>.Empty;

    internal ITestScriptResultRecorder Recorder { get; init; } = new TestScriptResultRecorder();

    public TestScriptContext WithResponse(string? responseId, FhirResponse response)
    {
        var ctx = this with { LastResponse = response };
        if (responseId is not null)
            ctx = ctx with { ResponseHistory = ResponseHistory.SetItem(responseId, response) };
        return ctx;
    }

    public TestScriptContext WithRequest(string? requestId, FhirRequest request)
    {
        var ctx = this with { LastRequest = request };
        if (requestId is not null)
            ctx = ctx with { RequestHistory = RequestHistory.SetItem(requestId, request) };
        return ctx;
    }

    public TestScriptContext WithVariable(string name, string value) =>
        this with { Variables = Variables.SetItem(name, value) };

    public TestScriptContext WithFixture(string id, JsonNode resource) =>
        this with { Fixtures = Fixtures.SetItem(id, resource) };
}
