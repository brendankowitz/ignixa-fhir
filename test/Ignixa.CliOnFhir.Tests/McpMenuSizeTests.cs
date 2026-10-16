// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;
using System.Text.Json;
using Ignixa.CliOnFhir.Mcp;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Shouldly;
using Xunit.Abstractions;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>
/// What the tool menu costs a client on every model request: the serialized <c>tools/list</c> result plus the server
/// instructions, read through an MCP client. Tokens are approximated as characters / 4. Run with
/// <c>--logger "console;verbosity=detailed"</c> to see the table. New at V3: b4250f44 measured 47,421 characters
/// (about 11.9K tokens) for 17 tools.
/// </summary>
public class McpMenuSizeTests(ITestOutputHelper output)
{
    // 4,000 held the nine tools of the agent-efficiency work at V7 (about 3,550 tokens); plan_fhir_search and validate_resources,
    // already cut to a few lines each with their detail in help, add about 470.
    private const int TokenBudget = 4100;

    [Fact]
    public async Task GivenEveryTool_WhenListedOverMcp_ThenTheMenuAndInstructionsStayUnderTheirBudget()
    {
        await using InProcessMcp mcp = await InProcessMcp.StartAsync(McpServerSetup.ToolTypes, [null]);
        IList<McpClientTool> tools = await mcp.Client.ListToolsAsync(cancellationToken: mcp.CancellationToken);
        string instructions = mcp.Client.ServerInstructions!;

        var report = new StringBuilder();
        int total = instructions.Length;
        report.AppendLine($"{"tool",-28} {"bytes",7} {"chars",7} {"~tokens",8}");
        foreach (Tool tool in tools.Select(tool => tool.ProtocolTool).OrderBy(tool => tool.Name, StringComparer.Ordinal))
        {
            string json = JsonSerializer.Serialize(tool, McpJsonUtilities.DefaultOptions);
            total += json.Length;
            report.AppendLine($"{tool.Name,-28} {Encoding.UTF8.GetByteCount(json),7} {json.Length,7} {json.Length / 4,8}");
        }

        report.AppendLine($"{"(instructions)",-28} {Encoding.UTF8.GetByteCount(instructions),7} {instructions.Length,7} {instructions.Length / 4,8}");
        report.AppendLine($"{"TOTAL (" + tools.Count + " tools)",-28} {"",7} {total,7} {total / 4,8}");
        output.WriteLine(report.ToString());

        (total / 4).ShouldBeLessThan(TokenBudget);
    }

    // Guards the registration the mcp verb uses: an unnamed WithTools(ToolTypes) bound to WithTools<T>(T target) and served no
    // tools at all.
    [Fact]
    public void GivenTheServerRegistration_WhenItsServicesAreBuilt_ThenEveryToolIsRegistered()
    {
        var services = new ServiceCollection();
        McpServerSetup.AddTools(services.AddMcpServer());
        using ServiceProvider provider = services.BuildServiceProvider();

        provider.GetServices<McpServerTool>().Select(tool => tool.ProtocolTool.Name).Order(StringComparer.Ordinal).ShouldBe(
        [
            "folder_code_systems", "folder_inspect", "help", "patient_cohort", "patient_context", "patient_list", "plan_fhir_search", "read_resources",
            "search_fhir_resources", "terminology", "terminology_load_package", "validate_resources",
        ]);
    }

    [Fact]
    public void GivenTheAssembly_WhenItsToolTypesAreCollected_ThenTheServerOffersEveryOne()
    {
        Type[] declared = [.. typeof(McpServerSetup).Assembly.GetTypes().Where(type => type.IsDefined(typeof(McpServerToolTypeAttribute), inherit: false))];

        McpServerSetup.ToolTypes.ShouldBe(declared, ignoreOrder: true);
    }
}
