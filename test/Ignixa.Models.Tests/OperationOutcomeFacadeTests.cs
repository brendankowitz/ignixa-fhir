// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Serialization.TestSupport;
using Shouldly;
using Xunit;

namespace Ignixa.Models.Tests;

public sealed class OperationOutcomeFacadeTests
{
    [Fact]
    public void GivenOperationOutcome_WhenReadBack_ThenSharedFieldsRoundTrip()
    {
        var outcome = new OperationOutcome
        {
            Text = new Narrative { Status = NarrativeStatus.Generated },
        };
        var issue = new OperationOutcomeIssue
        {
            Diagnostics = "Resource not found",
        };
        issue.Expression.Add("Patient.id");
        issue.Location.Add("Patient.id");
        outcome.Issue.Add(issue);

        outcome.ResourceType.ShouldBe("OperationOutcome");
        outcome.Text!.Status.ShouldBe(NarrativeStatus.Generated);
        outcome.Issue.Single().Diagnostics.ShouldBe("Resource not found");
        outcome.Issue.Single().Expression.Single().ShouldBe("Patient.id");
        outcome.Issue.Single().Location.Single().ShouldBe("Patient.id");
    }

    [Fact]
    public void GivenOperationOutcomeIssue_WhenDetailsSet_ThenCodeableConceptRoundTrips()
    {
        var issue = new OperationOutcomeIssue
        {
            Details = new CodeableConcept { Text = "not-found" },
        };
        issue.Details!.Coding.Add(new Coding { System = "http://hl7.org/fhir/tools/CodeSystem/tx-issue-type", Code = "not-in-vs" });

        issue.Details!.Text.ShouldBe("not-found");
        issue.Details!.Coding.Single().Code.ShouldBe("not-in-vs");
    }

    [Fact]
    public void GivenOperationOutcomeIssue_WhenSeverityCodeAndIssueTypeCodeSet_ThenLiteralsRoundTrip()
    {
        var issue = new OperationOutcomeIssue
        {
            SeverityCode = OperationOutcomeIssue.IssueSeverityCode.Error,
            IssueTypeCode = OperationOutcomeIssue.IssueTypeCommon.NotFound,
        };

        issue.SeverityCode.ShouldBe(OperationOutcomeIssue.IssueSeverityCode.Error);
        issue.IssueTypeCode.ShouldBe(OperationOutcomeIssue.IssueTypeCommon.NotFound);
        issue.MutableNode()["severity"]!.GetValue<string>().ShouldBe("error");
        issue.MutableNode()["code"]!.GetValue<string>().ShouldBe("not-found");
    }

    [Fact]
    public void GivenOperationOutcomeIssue_WhenSeverityNotSet_ThenSeverityCodeIsNull()
    {
        var issue = new OperationOutcomeIssue();

        issue.SeverityCode.ShouldBeNull();
        issue.IssueTypeCode.ShouldBeNull();
    }

    [Fact]
    public void GivenOperationOutcomeIssue_WhenRawSeverityIsR5OnlyLiteral_ThenSeverityCodeIsNull()
    {
        // "success" is an R5-only addition to the issue-severity value set (R4/R5-common
        // subset lives on the shared base, matching Bundle.Type's enum-drift handling).
        var issue = new OperationOutcomeIssue();
        issue.MutableNode()["severity"] = "success";

        issue.SeverityCode.ShouldBeNull();
    }

    [Fact]
    public void GivenOperationOutcomeIssue_WhenRawCodeIsR5OnlyLiteral_ThenIssueTypeCodeIsNull()
    {
        // "limited-filter"/"success" are R5-only additions to the issue-type value set -- same
        // R4/R5-common-subset handling as severity.
        var issue = new OperationOutcomeIssue();
        issue.MutableNode()["code"] = "success";

        issue.IssueTypeCode.ShouldBeNull();
    }

    [Fact]
    public void GivenR5OperationOutcomeIssue_WhenCodeSetToR5OnlyLiteral_ThenRoundTrips()
    {
        // Locks in that Ignixa.Models.R5.OperationOutcomeIssue.Code resolves to the generated
        // per-version Ignixa.Models.R5.IssueType (33 literals), not the shared base's nested
        // OperationOutcomeIssue.IssueTypeCommon (31 literals, R4/R5-common subset) -- the two
        // were previously both named "IssueType", which C#'s inherited-nested-type lookup
        // silently resolved to the wrong (narrower) one for every R4/R5 subclass.
        var issue = new Ignixa.Models.R5.OperationOutcomeIssue
        {
            Code = Ignixa.Models.R5.IssueType.LimitedFilter,
        };

        issue.Code.ShouldBe(Ignixa.Models.R5.IssueType.LimitedFilter);
        issue.MutableNode()["code"]!.GetValue<string>().ShouldBe("limited-filter");
    }

    [Fact]
    public void GivenR4OperationOutcomeIssue_WhenCodeSet_ThenUsesR4IssueType()
    {
        var issue = new Ignixa.Models.R4.OperationOutcomeIssue
        {
            Code = Ignixa.Models.R4.IssueType.NotFound,
        };

        issue.Code.ShouldBe(Ignixa.Models.R4.IssueType.NotFound);
    }
}
