// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Mcp;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
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
    // V13 cut the menu and instructions from 16,384 characters (4,096 tokens) to about 11,900 (2,984) for twelve tools. V14
    // (ADR 2610) serves the query toolset by default, held well under that; all keeps every tool and adds patient_summary.
    // Task 12b fix round 1 raised all to 3,400 for draft_resource and the listed asOf of search and plan (ADR 2610 section 5).
    private const int QueryTokenBudget = 2000;

    private const int AllTokenBudget = 3400;

    [Theory]
    [InlineData(McpToolset.Query, QueryTokenBudget)]
    [InlineData(McpToolset.All, AllTokenBudget)]
    public async Task GivenAToolset_WhenListedOverMcp_ThenTheMenuAndInstructionsStayUnderItsBudget(McpToolset toolset, int budget)
    {
        await using InProcessMcp mcp = await InProcessMcp.StartAsync(toolset, McpServerSetup.ToolTypes, [null]);
        IList<McpClientTool> tools = await mcp.Client.ListToolsAsync(cancellationToken: mcp.CancellationToken);

        int total = MenuSize.Measure(tools, mcp.Client.ServerInstructions!, output);

        (total / 4).ShouldBeLessThan(budget);
    }

    // Guards the registration the mcp verb uses: an unnamed WithTools(ToolTypes) bound to WithTools<T>(T target) and served no
    // tools at all.
    [Theory]
    [InlineData(McpToolset.Query, new[] { "folder_code_systems", "help", "patient_cohort", "patient_summary", "read_resources", "search_fhir_resources", "terminology" })]
    [InlineData(
        McpToolset.All,
        new[] { "draft_resource", "folder_code_systems", "folder_inspect", "help", "patient_cohort", "patient_context", "patient_list", "patient_summary", "plan_fhir_search", "read_resources", "search_fhir_resources", "terminology", "terminology_load_package", "validate_resources" })]
    public void GivenTheServerRegistration_WhenItsServicesAreBuilt_ThenTheToolsetsToolsAreRegistered(McpToolset toolset, string[] expected)
    {
        var services = new ServiceCollection();
        McpServerSetup.AddTools(services.AddMcpServer(), toolset);
        using ServiceProvider provider = services.BuildServiceProvider();

        provider.GetServices<McpServerTool>().Select(tool => tool.ProtocolTool.Name).Order(StringComparer.Ordinal).ShouldBe(expected);
        provider.GetServices<McpServerResource>().Select(resource => resource.ProtocolResourceTemplate.UriTemplate).ShouldBe([McpServerSetup.PatientSummaryTemplate]);
        provider.GetRequiredService<McpToolset>().ShouldBe(toolset);
    }

    [Fact]
    public void GivenTheAssembly_WhenItsToolTypesAreCollected_ThenTheServerOffersEveryOne()
    {
        Type[] declared = [.. typeof(McpServerSetup).Assembly.GetTypes().Where(type => type.IsDefined(typeof(McpServerToolTypeAttribute), inherit: false))];
        Type[] resources = [.. typeof(McpServerSetup).Assembly.GetTypes().Where(type => type.IsDefined(typeof(McpServerResourceTypeAttribute), inherit: false))];

        McpServerSetup.ToolTypes.ShouldBe(declared, ignoreOrder: true);
        McpServerSetup.ResourceTypes.ShouldBe(resources, ignoreOrder: true);
    }
}
