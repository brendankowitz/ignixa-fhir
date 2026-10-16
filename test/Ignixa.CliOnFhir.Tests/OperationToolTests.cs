// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.ComponentModel;
using System.Reflection;
using System.Text.Json.Nodes;
using Ignixa.CliOnFhir.Mcp;
using ModelContextProtocol.Protocol;
using Shouldly;
using static Ignixa.CliOnFhir.Tests.TerminologyFixture;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>
/// The <c>terminology</c> and <c>patient_context</c> tools, called through an MCP client: each operation reaches the
/// behaviour its former tool had, and an operation or argument it does not take fails the call naming what it does take.
/// New at V3: b4250f44 had eight terminology_* and three context_* tools instead.
/// </summary>
public class OperationToolTests
{
    [Theory]
    [InlineData("lookup", """{"system":"http://example.org/fhir/CodeSystem/body","code":"heart"}""", "outcome", "found")]
    [InlineData("search", """{"text":"heart"}""", "text", "heart")]
    [InlineData("validate", """{"code":"heart","system":"http://example.org/fhir/CodeSystem/body"}""", "result", "valid")]
    [InlineData("subsumes", """{"system":"http://example.org/fhir/CodeSystem/body","codeA":"disorder","codeB":"mi"}""", "outcome", "subsumes")]
    [InlineData("navigate", """{"system":"http://example.org/fhir/CodeSystem/body","code":"disorder","direction":"descendants","depth":2}""", "direction", "descendants")]
    [InlineData("expand", """{"valueSet":"http://example.org/fhir/ValueSet/heart","count":5}""", "complete", "true")]
    [InlineData("translate", """{"system":"http://example.org/fhir/CodeSystem/body","code":"mi"}""", "outcome", "mapped")]
    [InlineData("list", """{"kind":"valueSets"}""", "kind", "valueSets")]
    public async Task GivenEachTerminologyOperation_WhenCalledOverMcp_ThenItAnswersAsItsFormerTool(string operation, string arguments, string fact, string expected)
    {
        using var folder = new TestFolder();
        using var store = new TestFolder();
        await using InProcessMcp mcp = await InProcessMcp.StartAsync([typeof(TerminologyTools)], OpenWithPackage(folder, store));

        (CallToolResult result, JsonNode envelope) = await CallAsync(mcp, "terminology", operation, arguments);

        result.IsError.ShouldBe(false, envelope["issues"]!.ToJsonString());
        envelope["facts"]![fact]!.ToString().ShouldBe(expected, StringCompareShould.IgnoreCase);
    }

    [Theory]
    [InlineData("terminology", "define", "{}", "'define' is not an operation of this tool: lookup | search | validate | subsumes | navigate | expand | translate | list.")]
    [InlineData("terminology", "lookup", """{"system":"http://example.org/fhir/CodeSystem/body","code":"heart","valueSet":"http://example.org/fhir/ValueSet/heart"}""", "operation=lookup does not take 'valueSet'. It takes system, code [version].")]
    [InlineData("terminology", "subsumes", """{"system":"http://example.org/fhir/CodeSystem/body","codeA":"disorder"}""", "operation=subsumes needs 'codeB'. It takes system, codeA, codeB [version].")]
    [InlineData("terminology", "search", """{"text":"heart","_sort":"display"}""", "'_sort' is not an argument of this tool.")]
    [InlineData("terminology", "lookup", """{"system":"http://example.org/fhir/CodeSystem/body","code":"  "}""", "operation=lookup needs 'code'.")]
    public async Task GivenAnInvalidTerminologyCall_WhenCalledOverMcp_ThenItFailsNamingWhatTheOperationTakes(string tool, string operation, string arguments, string message)
    {
        using var folder = new TestFolder();
        using var store = new TestFolder();
        await using InProcessMcp mcp = await InProcessMcp.StartAsync([typeof(TerminologyTools)], OpenWithPackage(folder, store));

        (CallToolResult result, JsonNode envelope) = await CallAsync(mcp, tool, operation, arguments);

        AssertInvalid(result, envelope, message);
    }

    [Fact]
    public async Task GivenOptionalArgumentsSentAsNull_WhenCalledOverMcp_ThenTheyCountAsNotGiven()
    {
        using var folder = new TestFolder();
        using var store = new TestFolder();
        await using InProcessMcp mcp = await InProcessMcp.StartAsync([typeof(TerminologyTools)], OpenWithPackage(folder, store));

        (CallToolResult result, JsonNode envelope) = await CallAsync(mcp, "terminology", "lookup", """{"system":"http://example.org/fhir/CodeSystem/body","code":"heart","valueSet":null,"count":null}""");

        result.IsError.ShouldBe(false, envelope["issues"]!.ToJsonString());
        ((string)envelope["facts"]!["display"]!).ShouldBe("Heart disorder");
    }

    [Theory]
    [InlineData("root", """{"patientId":"p1"}""", "demographics")]
    [InlineData("expand", """{"patientId":"p1","nodeKey":"problems","count":5}""", "nodeKey")]
    [InlineData("find", """{"patientId":"p1","query":"asthma"}""", "query")]
    public async Task GivenEachPatientContextOperation_WhenCalledOverMcp_ThenItAnswersAsItsFormerTool(string operation, string arguments, string fact)
    {
        using TestFolder folder = PatientFixture.Ndjson();
        await using InProcessMcp mcp = await InProcessMcp.StartAsync([typeof(PatientTools)], PatientFixture.Open(folder));

        (CallToolResult result, JsonNode envelope) = await CallAsync(mcp, "patient_context", operation, arguments);

        result.IsError.ShouldBe(false, envelope["issues"]!.ToJsonString());
        envelope["facts"]![fact].ShouldNotBeNull();
    }

    [Theory]
    [InlineData("root", """{"patientId":"p1","nodeKey":"problems"}""", "operation=root does not take 'nodeKey'. It takes patientId.")]
    [InlineData("expand", """{"patientId":"p1"}""", "operation=expand needs 'nodeKey'. It takes patientId, nodeKey [count, offset, elements, maxBytes].")]
    [InlineData("find", """{"patientId":"p1","query":"asthma","nodeKey":"problems"}""", "operation=find does not take 'nodeKey'.")]
    [InlineData("summary", """{"patientId":"p1"}""", "'summary' is not an operation of this tool: root | expand | find.")]
    [InlineData("", """{"patientId":"p1"}""", "'operation' is required: root | expand | find.")]
    public async Task GivenAnInvalidPatientContextCall_WhenCalledOverMcp_ThenItFailsNamingWhatTheOperationTakes(string operation, string arguments, string message)
    {
        using TestFolder folder = PatientFixture.Ndjson();
        await using InProcessMcp mcp = await InProcessMcp.StartAsync([typeof(PatientTools)], PatientFixture.Open(folder));

        (CallToolResult result, JsonNode envelope) = await CallAsync(mcp, "patient_context", operation, arguments);

        AssertInvalid(result, envelope, message);
    }

    [Fact]
    public void GivenTheOperationTools_WhenDescribed_ThenEachOperationIsListedWithExactlyTheArgumentsItTakes()
    {
        AssertDescribed(typeof(TerminologyTools), nameof(TerminologyTools.Terminology), TerminologyTools.Operations);
        AssertDescribed(typeof(PatientTools), nameof(PatientTools.PatientContext), PatientTools.ContextOperations);
    }

    private static void AssertDescribed(Type type, string methodName, IReadOnlyList<ToolOperation> operations)
    {
        MethodInfo method = type.GetMethod(methodName)!;
        string description = method.GetCustomAttribute<DescriptionAttribute>()!.Description;
        IReadOnlySet<string> arguments = ToolArguments.Names(method);

        foreach (ToolOperation operation in operations)
        {
            description.ShouldContain($"{operation.Name}: {operation.Signature}");
            operation.Required.Concat(operation.Optional).ShouldAllBe(argument => arguments.Contains(argument));
        }

        arguments.Where(argument => argument != "operation")
            .ShouldAllBe(argument => operations.Any(operation => operation.Takes(argument)), "every argument belongs to some operation");
    }

    private static void AssertInvalid(CallToolResult result, JsonNode envelope, string message)
    {
        result.IsError.ShouldBe(true);
        envelope["facts"].ShouldBeNull();
        JsonNode issue = envelope["issues"]!.AsArray().Single(item => (string)item!["severity"]! == "error")!;
        ((string)issue["code"]!).ShouldBe("invalid-parameter");
        ((string)issue["diagnostics"]!).ShouldContain(message);
    }

    private static async Task<(CallToolResult Result, JsonNode Envelope)> CallAsync(InProcessMcp mcp, string tool, string operation, string arguments)
    {
        Dictionary<string, object?> call = new(StringComparer.Ordinal) { ["operation"] = operation };
        foreach ((string name, JsonNode? value) in JsonNode.Parse(arguments)!.AsObject())
        {
            call[name] = value?.DeepClone();
        }

        CallToolResult result = await mcp.Client.CallToolAsync(tool, call, cancellationToken: mcp.CancellationToken);
        return (result, JsonNode.Parse(result.Content.OfType<TextContentBlock>().Single().Text)!);
    }
}
