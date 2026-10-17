// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.CliOnFhir.Mcp;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Shouldly;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>
/// search_fhir_resources' per-call <c>asOf</c> (Task 12b): listed under the query toolset, which has room in its budget; under all,
/// whose menu is at its budget, accepted though not listed. The help topics say so. New behaviour: before it there was no asOf.
/// </summary>
public class SearchAsOfArgumentTests
{
    [Theory]
    [InlineData(McpToolset.Query, true)]
    [InlineData(McpToolset.All, false)]
    public async Task GivenAToolset_WhenTheSearchIsCalledWithAsOf_ThenItIsAcceptedAndListedUnderQueryOnly(McpToolset toolset, bool listed)
    {
        using TestFolder folder = SearchFixture.Clinic();
        await using InProcessMcp mcp = await InProcessMcp.StartAsync(toolset, [typeof(ResourceTools)], SearchFixture.Open(folder));

        McpClientTool search = (await mcp.Client.ListToolsAsync(cancellationToken: mcp.CancellationToken)).Single(tool => tool.Name == "search_fhir_resources");
        CallToolResult result = await mcp.Client.CallToolAsync(
            "search_fhir_resources",
            new Dictionary<string, object?> { ["resourceType"] = "Patient", ["asOf"] = "2020-01-01" },
            cancellationToken: mcp.CancellationToken);

        search.ProtocolTool.InputSchema.GetProperty("properties").TryGetProperty("asOf", out _).ShouldBe(listed);
        result.IsError.ShouldNotBe(true);
        ((string)JsonNode.Parse(result.Content.OfType<TextContentBlock>().Single().Text)!["asOf"]!["source"]!).ShouldBe("argument");
    }

    [Theory]
    [InlineData(McpToolset.All, true)]
    [InlineData(McpToolset.Query, false)]
    public void GivenAToolset_WhenTheDraftTopicIsAsked_ThenItIsThereUnderAllOnly(McpToolset toolset, bool present)
    {
        string text = new HelpTools(toolset).Help("draft").Content.OfType<TextContentBlock>().Single().Text;

        text.StartsWith("draft_resource:", StringComparison.Ordinal).ShouldBe(present);
    }
}
