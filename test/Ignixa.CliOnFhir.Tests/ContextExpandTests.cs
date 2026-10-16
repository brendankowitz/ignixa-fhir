// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Patients;
using Ignixa.CliOnFhir.Session;
using Shouldly;
using static Ignixa.CliOnFhir.Tests.PatientFixture;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>
/// <c>context_expand</c>. New capability: at 523e80d no patient tool existed, so every test here fails there (it does not
/// compile).
/// </summary>
public class ContextExpandTests
{
    [Theory]
    [InlineData("ndjson")]
    [InlineData("bundle")]
    public void GivenAProblemNode_WhenExpanded_ThenMedicationsLinkedByReasonReferenceFollowTheCondition(string layout)
    {
        // In the Bundle, m1's reasonReference is a urn:uuid; it links through the resolved reference.
        using TestFolder folder = Layout(layout);

        NodeFacts facts = Expand(folder, "problem/Condition-c1");

        facts.Items.Select(item => $"{item.Reference} {item.LinkedBy}").ShouldBe(["Condition/c1 ", "MedicationRequest/m1 reasonReference"]);
        facts.Rule.ShouldBe(PatientContext.ProblemLinkRule);
        facts.Resources.ShouldNotBeNull().Select(resource => (string)resource["id"]!).ShouldBe(["c1", "m1"]);
    }

    [Theory]
    [InlineData("ndjson")]
    [InlineData("bundle")]
    public void GivenAProblemNode_WhenAMedicationGivesItsCodeAsReason_ThenItIsLinkedByReasonCode(string layout)
    {
        using TestFolder folder = Layout(layout);

        NodeFacts facts = Expand(folder, "problem/Condition-c2");

        facts.Items.Select(item => $"{item.Reference} {item.LinkedBy}").ShouldBe(["Condition/c2 ", "MedicationRequest/m2 reasonCode"]);
    }

    [Fact]
    public void GivenTheBundleLayout_WhenANodeIsExpanded_ThenResourcesKeepTheirUrnsAndResolvedReferencesMapThem()
    {
        using TestFolder folder = Bundle();

        NodeFacts facts = Expand(folder, "problem/Condition-c1");

        ((string)facts.Resources![1]["reasonReference"]![0]!["reference"]!).ShouldBe(Urn("Condition/c1"));
        facts.ResolvedReferences.ShouldNotBeNull()["MedicationRequest/m1"].ShouldBe(
            new Dictionary<string, string> { [Urn("Patient/p1")] = "Patient/p1", [Urn("Condition/c1")] = "Condition/c1" },
            ignoreOrder: true);
    }

    [Theory]
    [InlineData("ndjson")]
    [InlineData("bundle")]
    public void GivenALabNode_WhenExpandedInPages_ThenTheWholeSeriesComesNewestFirstWithNextForTheRest(string layout)
    {
        using TestFolder folder = Layout(layout);
        FolderSession session = Open(folder);

        ToolEnvelope<NodeFacts?> first = ContextExpander.Expand(session, "p1", "labs/http://loinc.org|8867-4", count: 3);
        ToolEnvelope<NodeFacts?> second = ContextExpander.Expand(session, "p1", "labs/http://loinc.org|8867-4", count: 3, offset: 3);

        first.Facts.ShouldNotBeNull().Items.Select(item => $"{item.Reference} {item.Value}").ShouldBe(["Observation/o3 80", "Observation/o2 70", "Observation/o1 60"]);
        first.Facts.Total.ShouldBe(4);
        first.Omitted.ShouldBe([new OmittedItem("1 more items (offset=3)", "limit")]);
        NextCall next = first.Next.ShouldHaveSingleItem();
        next.Tool.ShouldBe("patient_context");
        next.Args.ShouldBe(new Dictionary<string, object> { ["operation"] = "expand", ["patientId"] = "p1", ["nodeKey"] = "labs/http://loinc.org|8867-4", ["offset"] = 3 }, ignoreOrder: true);
        second.Facts.ShouldNotBeNull().Items.ShouldHaveSingleItem().Reference.ShouldBe("Observation/o0");
        second.Next.ShouldBeEmpty();
        first.Evidence.Select(item => item.Reference).ShouldBe(["Observation/o3", "Observation/o2", "Observation/o1"]);
    }

    [Theory]
    [InlineData("ndjson")]
    [InlineData("bundle")]
    public void GivenAnEncounterNode_WhenExpanded_ThenTheResourcesReferencingItFollowIt(string layout)
    {
        using TestFolder folder = Layout(layout);

        NodeFacts facts = Expand(folder, "encounter/Encounter-e5");

        facts.Items.Select(item => $"{item.Reference} {item.NodeKey}").ShouldBe(["Encounter/e5 encounter/Encounter-e5", "Observation/o1 labs/http://loinc.org|8867-4"]);
    }

    [Fact]
    public void GivenTheLabsNode_WhenExpanded_ThenItListsTheCodesWithoutResources()
    {
        using TestFolder folder = Ndjson();

        ToolEnvelope<NodeFacts?> envelope = ContextExpander.Expand(Open(folder), "p1", "labs", elements: "code");

        envelope.Facts.ShouldNotBeNull().Items.Count.ShouldBe(5);
        envelope.Facts.Resources.ShouldBeNull();
        envelope.Issues.ShouldHaveSingleItem().Code.ShouldBe("parameter-ignored");
    }

    [Fact]
    public void GivenTheHostileLabCode_WhenItsDigestKeyIsExpanded_ThenTheSeriesIsFound()
    {
        using TestFolder folder = Ndjson();
        string key = ContextRoot.Get(Open(folder), "p1").Facts!.Labs.Items.Single(item => item.Reference == "Observation/o14").NodeKey!;

        ToolEnvelope<NodeFacts?> envelope = ContextExpander.Expand(Open(folder), "p1", key);

        envelope.Facts.ShouldNotBeNull().Items.ShouldHaveSingleItem().Reference.ShouldBe("Observation/o14");
        JsonSerializer.Serialize(envelope.Facts.Items, EnvelopeJson.Compact).ShouldNotContain(Hostile);
        JsonSerializer.Serialize(envelope.Next, EnvelopeJson.Compact).ShouldNotContain(HostileCode);
    }

    [Fact]
    public void GivenTheAllergiesNodeOfAPatientWithNone_WhenExpanded_ThenAbsentSaysWhatWasChecked()
    {
        using TestFolder folder = Ndjson();

        ToolEnvelope<NodeFacts?> envelope = ContextExpander.Expand(Open(folder), "p1", "allergies");

        envelope.Facts.ShouldNotBeNull().Total.ShouldBe(0);
        envelope.Absent.ShouldHaveSingleItem().What.ShouldBe("AllergyIntolerance");
    }

    [Theory]
    [InlineData("problem/Condition-c9", "unknown-node-key", "'problem/Condition-c9' names no node in Patient/p1's context")]
    [InlineData("problem/Condition-c4", "unknown-node-key", "'problem/Condition-c4' names no node in Patient/p1's context")]
    [InlineData("labs/http://loinc.org|0000-0", "unknown-node-key", "names no node")]
    [InlineData("conditions", "invalid-node-key", "'conditions' is not a node key.")]
    [InlineData("problem/Condition-a b", "invalid-node-key", "is not a node key")]
    [InlineData("labs/http://loinc.org", "invalid-node-key", "is not a node key")]
    public void GivenANodeKeyThatNamesNoNodeOfThePatient_WhenExpanded_ThenTheCallFailsWithARequestError(string nodeKey, string code, string diagnostics)
    {
        // c4 is p2's condition.
        using TestFolder folder = Ndjson();

        ToolEnvelope<NodeFacts?> envelope = ContextExpander.Expand(Open(folder), "p1", nodeKey);

        envelope.Facts.ShouldBeNull();
        IssueItem issue = envelope.Issues.ShouldHaveSingleItem();
        issue.Severity.ShouldBe("error");
        issue.Code.ShouldBe(code);
        issue.Diagnostics.ShouldContain(diagnostics);
    }

    [Theory]
    [InlineData(0, 0, "'count' must be at least 1")]
    [InlineData(null, -1, "'offset' must not be negative")]
    public void GivenBadPaging_WhenExpanded_ThenTheCallFails(int? count, int offset, string diagnostics)
    {
        using TestFolder folder = Ndjson();

        ToolEnvelope<NodeFacts?> envelope = ContextExpander.Expand(Open(folder), "p1", "encounters", count, offset);

        envelope.Facts.ShouldBeNull();
        envelope.Issues.ShouldHaveSingleItem().Diagnostics.ShouldStartWith(diagnostics);
    }

    private static NodeFacts Expand(TestFolder folder, string nodeKey) => ContextExpander.Expand(Open(folder), "p1", nodeKey).Facts.ShouldNotBeNull();
}
