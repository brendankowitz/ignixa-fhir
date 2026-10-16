// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.IO.Pipelines;
using System.Text.Json.Nodes;
using Ignixa.CliOnFhir.Mcp;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Shouldly;
using static Ignixa.CliOnFhir.Tests.SearchFixture;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>
/// <c>search_fhir_resources</c> called through an MCP client, as an agent calls it: <c>columns</c> bound from a JSON object,
/// and an argument the tool does not define (<c>_sort</c> beside <c>query</c>) reported instead of dropped. New behaviour: at
/// ab2447f3 the SDK dropped an unknown argument silently, so a top-level <c>_sort</c> returned an unsorted page.
/// </summary>
public class SearchToolArgumentTests
{
    [Fact]
    public async Task GivenColumnsAsAJsonObject_WhenCalledOverMcp_ThenTheTableIsReturnedInTheirOrder()
    {
        using TestFolder folder = ProjectionFixture.Synthea();

        JsonNode result = await CallAsync(folder, new Dictionary<string, object?>
        {
            ["resourceType"] = "MedicationRequest",
            ["compartment"] = "Patient/pa",
            ["query"] = "_sort=authoredon",
            ["columns"] = new Dictionary<string, string> { ["prescriber"] = "requester.resolve().name.first().family", ["date"] = "authoredOn" },
        });

        result["facts"]!["columns"]!.ToJsonString().ShouldBe("""["ref","prescriber","date"]""");
        result["facts"]!["rows"]![0]!.ToJsonString().ShouldBe("""["MedicationRequest/m1","Marquardt","2019-02-01"]""");
    }

    [Fact]
    public async Task GivenSortAsATopLevelArgument_WhenCalledOverMcp_ThenTheCallFailsSayingItBelongsInQuery()
    {
        using TestFolder folder = ProjectionFixture.Synthea();

        JsonNode result = await CallAsync(folder, new Dictionary<string, object?> { ["resourceType"] = "MedicationRequest", ["_sort"] = "-authoredon" });

        result["facts"].ShouldBeNull();
        JsonNode issue = result["issues"]!.AsArray().Single(item => (string)item!["severity"]! == "error")!;
        ((string)issue["code"]!).ShouldBe("invalid-parameter");
        ((string)issue["diagnostics"]!).ShouldContain("'_sort' is not an argument");
        ((string)issue["diagnostics"]!).ShouldContain("goes inside 'query'");
    }

    [Fact]
    public async Task GivenAnUnknownArgumentAndLenient_WhenCalledOverMcp_ThenTheSearchRunsWithAParameterIgnoredWarning()
    {
        using TestFolder folder = ProjectionFixture.Synthea();

        JsonNode result = await CallAsync(folder, new Dictionary<string, object?> { ["resourceType"] = "MedicationRequest", ["_count"] = 1, ["lenient"] = true });

        ((int)result["facts"]!["total"]!).ShouldBe(4);
        result["issues"]!.AsArray().ShouldContain(item => (string)item!["code"]! == "parameter-ignored" && ((string)item["diagnostics"]!).Contains("'_count'", StringComparison.Ordinal));
    }

    [Fact]
    public void GivenTheSearchTool_WhenItsSchemaIsBuilt_ThenColumnsAndFhirPathAreArgumentsAndTheInjectedContextIsNot()
    {
        McpServerTool tool = McpServerTool.Create(typeof(ResourceTools).GetMethod(nameof(ResourceTools.SearchFhirResources))!, new ResourceTools(null!));

        JsonNode properties = JsonNode.Parse(tool.ProtocolTool.InputSchema.GetRawText())!["properties"]!;

        properties.AsObject().Select(property => property.Key).ShouldBe(
            ["resourceType", "query", "count", "offset", "summary", "elements", "lenient", "compartment", "maxBytes", "columns", "fhirpath", "perPatient"],
            ignoreOrder: true);
        properties["columns"]!["type"]!.ToJsonString().ShouldContain("\"object\"");
    }

    private static async Task<JsonNode> CallAsync(TestFolder folder, Dictionary<string, object?> arguments)
    {
        var clientToServer = new Pipe();
        var serverToClient = new Pipe();
        var tools = new ResourceTools(Open(folder));
        var options = new McpServerOptions
        {
            ToolCollection = [McpServerTool.Create(typeof(ResourceTools).GetMethod(nameof(ResourceTools.SearchFhirResources))!, tools)],
        };
        McpServerSetup.Configure(options);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        await using var serverTransport = new StreamServerTransport(clientToServer.Reader.AsStream(), serverToClient.Writer.AsStream());
        await using McpServer server = McpServer.Create(serverTransport, options);
        _ = server.RunAsync(cancellation.Token);
        await using McpClient client = await McpClient.CreateAsync(
            new StreamClientTransport(clientToServer.Writer.AsStream(), serverToClient.Reader.AsStream()),
            cancellationToken: cancellation.Token);

        CallToolResult result = await client.CallToolAsync("search_fhir_resources", arguments, cancellationToken: cancellation.Token);
        return JsonNode.Parse(result.Content.OfType<TextContentBlock>().Single().Text)!;
    }
}
