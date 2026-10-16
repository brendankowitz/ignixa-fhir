// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.ComponentModel;
using System.IO.Pipelines;
using System.Reflection;
using Ignixa.CliOnFhir.Mcp;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Shouldly;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>
/// The server tells the client the cheap paths when it connects. New capability: at f8b47276 the initialize result carried
/// no instructions.
/// </summary>
public class McpInstructionsTests
{
    [Fact]
    public async Task GivenAConnectedClient_WhenTheServerInitializes_ThenTheInstructionsAreInTheInitializeResult()
    {
        var clientToServer = new Pipe();
        var serverToClient = new Pipe();
        var options = new McpServerOptions();
        McpServerSetup.Configure(options);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        await using var serverTransport = new StreamServerTransport(clientToServer.Reader.AsStream(), serverToClient.Writer.AsStream());
        await using McpServer server = McpServer.Create(serverTransport, options);
        _ = server.RunAsync(cancellation.Token);
        await using McpClient client = await McpClient.CreateAsync(
            new StreamClientTransport(clientToServer.Writer.AsStream(), serverToClient.Reader.AsStream()),
            cancellationToken: cancellation.Token);

        client.ServerInstructions.ShouldBe(McpServerSetup.Instructions);
        client.ServerInfo.Name.ShouldBe("cli-on-fhir");
    }

    [Theory]
    [InlineData("compartment=Patient/<id>")]
    [InlineData("identifier")]
    [InlineData("patient_list")]
    [InlineData("elements")]
    [InlineData("summary=count")]
    [InlineData("_sort=-date")]
    [InlineData("count=1")]
    [InlineData("_include")]
    [InlineData("conditional")]
    [InlineData("issues")]
    [InlineData("columns={")]
    [InlineData("requester.resolve()")]
    [InlineData("medication.ofType(Reference).resolve()")]
    [InlineData("fhirpath")]
    [InlineData("goes inside query")]
    public void GivenTheInstructions_WhenRead_ThenTheyNameEachCheapPath(string mention)
    {
        McpServerSetup.Instructions.ShouldContain(mention);
    }

    [Fact]
    public void GivenTheInstructions_WhenMeasured_ThenTheyStayShort()
    {
        McpServerSetup.Instructions.Length.ShouldBeLessThan(1500);
    }

    [Fact]
    public void GivenThePatientListTool_WhenDescribed_ThenItSaysToSkipItWhenTheIdIsKnown()
    {
        string description = typeof(PatientTools).GetMethod(nameof(PatientTools.PatientList))!.GetCustomAttribute<DescriptionAttribute>()!.Description;

        description.ShouldContain("if you know the patient's id or MRN, skip this and search with compartment=Patient/<id>", Case.Insensitive);
    }

    [Fact]
    public void GivenTheSearchTool_WhenDescribed_ThenItPointsAtTheCheapPathsAndNoLongerSaysToInspectFirst()
    {
        string description = typeof(ResourceTools).GetMethod(nameof(ResourceTools.SearchFhirResources))!.GetCustomAttribute<DescriptionAttribute>()!.Description;

        description.ShouldContain("compartment=Patient/<id>");
        description.ShouldContain("summary=count");
        description.ShouldNotContain("folder_inspect first");
    }

    // New at V2: the description steers to columns with resolve() and to fhirpath, and says where _sort goes.
    [Theory]
    [InlineData("columns={\"date\":\"authoredOn\"")]
    [InlineData("requester.resolve().name.first().family")]
    [InlineData("medication.ofType(Reference).resolve().code")]
    [InlineData("'fhirpath'")]
    [InlineData("_sort and _count included, goes inside 'query'")]
    public void GivenTheSearchTool_WhenDescribed_ThenItShowsColumnsAndFhirPathAndWhereSearchParametersGo(string mention)
    {
        string description = typeof(ResourceTools).GetMethod(nameof(ResourceTools.SearchFhirResources))!.GetCustomAttribute<DescriptionAttribute>()!.Description;

        description.ShouldContain(mention);
    }
}
