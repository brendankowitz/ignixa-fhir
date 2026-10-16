// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.CliOnFhir.Mcp;
using ModelContextProtocol.Protocol;
using Shouldly;
using static Ignixa.CliOnFhir.Tests.SearchFixture;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>
/// A failed call reaches an MCP client as an error result whose text is the whole envelope, with the issue that explains it.
/// Guard, not regression: the V8 benchmark logged two perPatient calls with count 200 as 2-byte results, and these exact calls
/// pass at 365d1acd too. The server sent the envelope with isError; Copilot puts an error result's text in
/// <c>error.message</c> and leaves <c>result.content</c> empty, which the byte count read as <c>""</c>.
/// </summary>
public class McpErrorResultTests
{
    private const string Ldl = "code=http://loinc.org|18262-6";

    private static TestFolder Labs() => new TestFolder().Write("data.ndjson", """
        {"resourceType":"Patient","id":"p1"}
        {"resourceType":"Observation","id":"o1","status":"final","code":{"coding":[{"system":"http://loinc.org","code":"18262-6"}]},"subject":{"reference":"Patient/p1"},"effectiveDateTime":"2025-01-01","valueQuantity":{"value":120}}
        """);

    public static TheoryData<string, Dictionary<string, object?>> FailedBenchmarkCalls => new()
    {
        {
            "invalid-parameter",
            new Dictionary<string, object?>
            {
                ["resourceType"] = "Observation",
                ["query"] = Ldl + "&_sort=-date",
                ["perPatient"] = "latest",
                ["count"] = 200,
                ["maxBytes"] = 16384,
                ["columns"] = new Dictionary<string, string> { ["p"] = "subject.reference", ["v"] = "value.ofType(Quantity).value" },
            }
        },
        {
            "invalid-parameter",
            new Dictionary<string, object?>
            {
                ["resourceType"] = "Observation",
                ["query"] = Ldl,
                ["perPatient"] = "latest",
                ["count"] = 200,
                ["maxBytes"] = 16384,
                ["elements"] = "id",
                ["columns"] = new Dictionary<string, string> { ["p"] = "subject.reference" },
            }
        },
    };

    [Theory]
    [MemberData(nameof(FailedBenchmarkCalls))]
    public async Task GivenACallTheToolRefuses_WhenCalledOverMcp_ThenTheErrorResultCarriesTheEnvelopeWithItsIssue(string code, Dictionary<string, object?> arguments)
    {
        using TestFolder folder = Labs();
        await using InProcessMcp mcp = await InProcessMcp.StartAsync([typeof(ResourceTools)], Open(folder));

        CallToolResult result = await mcp.Client.CallToolAsync("search_fhir_resources", arguments, cancellationToken: mcp.CancellationToken);

        result.IsError.ShouldBe(true);
        string text = result.Content.OfType<TextContentBlock>().ShouldHaveSingleItem().Text;
        JsonNode envelope = JsonNode.Parse(text)!;
        envelope["facts"].ShouldBeNull();
        JsonNode issue = envelope["issues"]!.AsArray().ShouldHaveSingleItem()!;
        ((string)issue["severity"]!).ShouldBe("error");
        ((string)issue["code"]!).ShouldBe(code);
        ((string)issue["diagnostics"]!).Length.ShouldBeGreaterThan(20);
    }

    [Fact]
    public async Task GivenPerPatientWithCount200AndColumns_WhenCalledOverMcp_ThenTheRowsComeBack()
    {
        using TestFolder folder = Labs();
        await using InProcessMcp mcp = await InProcessMcp.StartAsync([typeof(ResourceTools)], Open(folder));
        var arguments = new Dictionary<string, object?>
        {
            ["resourceType"] = "Observation",
            ["query"] = Ldl,
            ["perPatient"] = "latest",
            ["count"] = 200,
            ["columns"] = new Dictionary<string, string> { ["v"] = "value.ofType(Quantity).value" },
        };

        CallToolResult result = await mcp.Client.CallToolAsync("search_fhir_resources", arguments, cancellationToken: mcp.CancellationToken);

        result.IsError.ShouldNotBe(true);
        JsonNode envelope = JsonNode.Parse(result.Content.OfType<TextContentBlock>().ShouldHaveSingleItem().Text)!;
        envelope["facts"]!["rows"]!.ToJsonString().ShouldBe("""[["Observation/o1",120]]""");
    }
}
