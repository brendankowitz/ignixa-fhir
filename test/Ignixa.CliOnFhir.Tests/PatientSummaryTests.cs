// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Mcp;
using Ignixa.CliOnFhir.Patients;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using Shouldly;
using static Ignixa.CliOnFhir.Tests.SearchFixture;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>
/// <c>patient_summary</c> and the <c>patient://{id}/summary</c> resource (ADR 2610): the patient map of the Patient an id, an
/// MRN or a <c>system|value</c> names. New capability: before V14 the map was only <c>patient_context</c> root, by id.
/// </summary>
public class PatientSummaryTests
{
    // m2 and m4 share MRN-2002 under different systems.
    private static TestFolder Patients() => new TestFolder().Write("patients.ndjson", """
        {"resourceType":"Patient","id":"m1","identifier":[{"system":"urn:mrn","value":"MRN-1001"}],"name":[{"use":"official","family":"Crist","given":["Madalyn"]}],"birthDate":"1994-12-29","gender":"female"}
        {"resourceType":"Patient","id":"m2","identifier":[{"system":"urn:mrn","value":"MRN-2002"}],"name":[{"use":"official","family":"Abbott","given":["Bo"]}],"birthDate":"1980-01-01","gender":"male"}
        {"resourceType":"Patient","id":"m4","identifier":[{"system":"urn:other","value":"MRN-2002"}],"name":[{"use":"official","family":"Abbott","given":["Al"]}],"birthDate":"1981-01-01","gender":"male"}
        {"resourceType":"Condition","id":"c1","subject":{"reference":"Patient/m1"},"clinicalStatus":{"coding":[{"system":"http://terminology.hl7.org/CodeSystem/condition-clinical","code":"active"}]},"code":{"coding":[{"system":"http://snomed.info/sct","code":"44054006","display":"Diabetes mellitus type 2"}]},"onsetDateTime":"2015-01-01"}
        """);

    [Theory]
    [InlineData("m1")]
    [InlineData("MRN-1001")]
    [InlineData("urn:mrn|MRN-1001")]
    [InlineData(" MRN-1001 ")]
    public void GivenAnIdAnMrnOrAnIdentifierToken_WhenSummarized_ThenTheNamedPatientsMapIsReturned(string patient)
    {
        using TestFolder folder = Patients();

        ToolEnvelope<PatientMapFacts?> envelope = PatientMap.Summarize(Open(folder), patient);

        PatientMapFacts facts = envelope.Facts.ShouldNotBeNull();
        facts.Demographics.Reference.ShouldBe("Patient/m1");
        facts.Domains["problems"].Count.ShouldBe(1);
    }

    [Fact]
    public void GivenAnIdentifierTokenNamingOneOfTwoSharingTheValue_WhenSummarized_ThenItsSystemDecides()
    {
        using TestFolder folder = Patients();

        PatientMap.Summarize(Open(folder), "urn:other|MRN-2002").Facts.ShouldNotBeNull().Demographics.Reference.ShouldBe("Patient/m4");
    }

    [Fact]
    public void GivenAnMrnNamingTwoPatients_WhenSummarized_ThenTheCallFailsAsAmbiguousWithBothIds()
    {
        using TestFolder folder = Patients();

        ToolEnvelope<PatientMapFacts?> envelope = PatientMap.Summarize(Open(folder), "MRN-2002");

        envelope.Facts.ShouldBeNull();
        IssueItem issue = envelope.Issues.ShouldHaveSingleItem();
        issue.Code.ShouldBe("ambiguous-patient");
        issue.Diagnostics.ShouldContain("names 2 patients");
        issue.Diagnostics.ShouldContain("m2, m4");
    }

    [Fact]
    public void GivenAnIdentifierNamingNobody_WhenSummarized_ThenTheCallFailsAsUnknownWithThePatientsWhoseFieldsContainIt()
    {
        using TestFolder folder = Patients();

        ToolEnvelope<PatientMapFacts?> envelope = PatientMap.Summarize(Open(folder), "MRN-10");

        envelope.Facts.ShouldBeNull();
        IssueItem issue = envelope.Issues.ShouldHaveSingleItem();
        issue.Code.ShouldBe("unknown-patient");
        issue.Diagnostics.ShouldContain("must match exactly");
        issue.Diagnostics.ShouldContain("contain it: m1");
    }

    [Fact]
    public void GivenAnUnknownValueResemblingNoPatient_WhenSummarized_ThenTheCallFailsAsUnknownWithoutCandidates()
    {
        using TestFolder folder = Patients();

        IssueItem issue = PatientMap.Summarize(Open(folder), "nobody").Issues.ShouldHaveSingleItem();

        issue.Code.ShouldBe("unknown-patient");
        issue.Diagnostics.ShouldNotContain("contain it");
        issue.Diagnostics.ShouldContain("3 Patient resources");
    }

    [Theory]
    [InlineData("")]
    [InlineData("two words")]
    public void GivenABlankOrSpacedValue_WhenSummarized_ThenTheCallFailsWithInvalidParameter(string patient)
    {
        using TestFolder folder = Patients();

        PatientMap.Summarize(Open(folder), patient).Issues.ShouldHaveSingleItem().Code.ShouldBe("invalid-parameter");
    }

    [Fact]
    public async Task GivenTheQueryToolset_WhenPatientSummaryIsCalledOverMcpWithAnMrn_ThenItReturnsTheLeanMap()
    {
        using TestFolder folder = Patients();
        await using InProcessMcp mcp = await InProcessMcp.StartAsync(McpToolset.Query, [typeof(PatientTools)], Open(folder));

        CallToolResult result = await mcp.Client.CallToolAsync("patient_summary", new Dictionary<string, object?> { ["patient"] = "MRN-1001" }, cancellationToken: mcp.CancellationToken);

        result.IsError.ShouldNotBe(true);
        JsonNode envelope = JsonNode.Parse(result.Content.OfType<TextContentBlock>().Single().Text)!;
        ((string)envelope["facts"]!["demographics"]!["reference"]!).ShouldBe("Patient/m1");
        envelope.AsObject().ContainsKey("derived").ShouldBeFalse();
    }

    [Fact]
    public async Task GivenPatientSummary_WhenCalledWithTheOldPatientIdArgument_ThenTheCallFailsNamingTheArguments()
    {
        using TestFolder folder = Patients();
        await using InProcessMcp mcp = await InProcessMcp.StartAsync(McpToolset.Query, [typeof(PatientTools)], Open(folder));

        CallToolResult result = await mcp.Client.CallToolAsync("patient_summary", new Dictionary<string, object?> { ["patient"] = "m1", ["patientId"] = "m1" }, cancellationToken: mcp.CancellationToken);

        result.IsError.ShouldBe(true);
        JsonNode envelope = JsonNode.Parse(result.Content.OfType<TextContentBlock>().Single().Text)!;
        ((string)envelope["issues"]![0]!["diagnostics"]!).ShouldBe("'patientId' is not an argument of this tool. Its arguments are patient.");
    }

    [Fact]
    public async Task GivenEitherToolset_WhenResourceTemplatesAreListed_ThenThePatientSummaryTemplateIsOffered()
    {
        using TestFolder folder = Patients();
        await using InProcessMcp mcp = await InProcessMcp.StartAsync(McpToolset.Query, [typeof(PatientResources)], Open(folder));

        IList<ModelContextProtocol.Client.McpClientResourceTemplate> templates = await mcp.Client.ListResourceTemplatesAsync(cancellationToken: mcp.CancellationToken);

        ResourceTemplate template = templates.ShouldHaveSingleItem().ProtocolResourceTemplate;
        template.UriTemplate.ShouldBe("patient://{id}/summary");
        template.Name.ShouldBe("patient_summary");
        template.MimeType.ShouldBe("application/json");
    }

    [Theory]
    [InlineData("patient://m1/summary")]
    [InlineData("patient://MRN-1001/summary")]
    [InlineData("patient://urn:mrn|MRN-1001/summary")]
    [InlineData("patient://urn%3Amrn%7CMRN-1001/summary")]
    public async Task GivenThePatientSummaryTemplate_WhenReadForAPatient_ThenItHoldsTheSameMapAsTheTool(string uri)
    {
        using TestFolder folder = Patients();
        await using InProcessMcp mcp = await InProcessMcp.StartAsync(McpToolset.Query, [typeof(PatientResources)], Open(folder));

        ReadResourceResult result = await ReadAsync(mcp, uri, mcp.CancellationToken);

        TextResourceContents contents = result.Contents.OfType<TextResourceContents>().ShouldHaveSingleItem();
        contents.MimeType.ShouldBe("application/json");
        JsonNode envelope = JsonNode.Parse(contents.Text)!;
        ((string)envelope["facts"]!["demographics"]!["reference"]!).ShouldBe("Patient/m1");
    }

    [Fact]
    public async Task GivenThePatientSummaryTemplate_WhenReadForAnAmbiguousMrn_ThenTheReadFailsWithTheIssue()
    {
        using TestFolder folder = Patients();
        await using InProcessMcp mcp = await InProcessMcp.StartAsync(McpToolset.Query, [typeof(PatientResources)], Open(folder));

        McpException failure = await Should.ThrowAsync<McpException>(() => ReadAsync(mcp, "patient://MRN-2002/summary", mcp.CancellationToken).AsTask());

        failure.Message.ShouldContain("ambiguous-patient");
    }

    // The string overload sends the URI as written. The Uri overload normalizes it first, and System.Uri lowercases the
    // authority, where the template puts the id: MRN-1001 would arrive as mrn-1001 and name nobody.
#pragma warning disable CA2234
    private static ValueTask<ReadResourceResult> ReadAsync(InProcessMcp mcp, string uri, CancellationToken cancellationToken) =>
        mcp.Client.ReadResourceAsync(uri, cancellationToken: cancellationToken);
#pragma warning restore CA2234
}
