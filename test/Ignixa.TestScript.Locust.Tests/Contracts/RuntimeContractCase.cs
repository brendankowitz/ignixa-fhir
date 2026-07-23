using System.Text.Json.Nodes;

namespace Ignixa.TestScript.Locust.Tests.Contracts;

/// <summary>
/// A single reviewed runtime contract case, deserialized from <c>Contracts/runtime-cases.json</c>. The JSON
/// document is the authoritative, immutable contract shared by the .NET <see cref="RuntimeContractTests"/> and
/// the Python <c>test_runtime_contract.py</c>; this type only exposes typed views over one case's node so the
/// .NET half can read the same fields the Python half reads. Nothing here mutates or regenerates the contract.
/// </summary>
public sealed class RuntimeContractCase(JsonObject root)
{
    private readonly JsonObject _root = root ?? throw new ArgumentNullException(nameof(root));

    /// <summary>Stable, human-readable case identifier (also the xUnit theory key).</summary>
    public string Name => Required(_root, "name").GetValue<string>();

    /// <summary>Short human description of what the case pins.</summary>
    public string Description => _root["description"]?.GetValue<string>() ?? string.Empty;

    /// <summary>The metric source / IR metadata source used when compiling this case.</summary>
    public string Source => Required(_root, "source").GetValue<string>();

    /// <summary>The FHIR version string the case targets, or <see langword="null"/>.</summary>
    public string? FhirVersion => _root["fhirVersion"]?.GetValue<string>();

    /// <summary>The raw input TestScript JSON parsed by the real <c>TestScriptParser</c>.</summary>
    public string InputJson => Required(_root, "input").ToJsonString();

    /// <summary>The canonical compiled IR both engines pin against (a parsed JSON object, not a string).</summary>
    public JsonNode CanonicalIr => Required(_root, "canonicalIr");

    /// <summary>The ordered queued HTTP responses (one per outbound HTTP attempt).</summary>
    public JsonArray Responses => Required(_root, "responses").AsArray();

    /// <summary>The ordered expected outbound requests (method, url, body, and headers-that-matter).</summary>
    public JsonArray ExpectedRequests => Required(_root, "expectedRequests").AsArray();

    /// <summary>The expected setup/tests/teardown aggregate outcomes.</summary>
    public JsonObject ExpectedPhases => Required(_root, "expectedPhases").AsObject();

    /// <summary>The expected .NET per-phase report action (kind, outcome) sequences.</summary>
    public JsonObject ExpectedReport => Required(_root, "expectedReport").AsObject();

    /// <summary>The exact evaluator-produced polling-exhaustion message, when the case pins one.</summary>
    public string? PollingTimeoutMessage => _root["expectedPollingTimeoutMessage"]?.GetValue<string>();

    private static JsonNode Required(JsonObject root, string property)
    {
        return root[property] ?? throw new InvalidOperationException(
            $"Runtime contract case is missing required property '{property}'.");
    }
}
