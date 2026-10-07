using Ignixa.Abstractions;
using Ignixa.Api.Endpoints;
using Ignixa.Application.BackgroundOperations.BulkDelete;
using Shouldly;
using Xunit;

namespace Ignixa.Api.Tests.Endpoints;

public sealed class BulkDeleteStatusResponseBuilderTests
{
    private static readonly IReadOnlyDictionary<string, long> NoCounts = new Dictionary<string, long>();

    [Fact]
    public void GivenRunningJobWithNoCounts_WhenBuilding_ThenResponseIs202WithInProgressIssueAndNoResourceDeletedCount()
    {
        var result = new GetBulkDeleteStatusResult("Running", NoCounts, [], null);

        var response = BulkDeleteStatusResponseBuilder.Build(result, FhirVersion.R4);

        response.StatusCode.ShouldBe(202);
        response.Body["resourceType"]!.GetValue<string>().ShouldBe("Parameters");
        var parameters = response.Body["parameter"]!.AsArray();
        parameters.Count.ShouldBe(1);
        parameters[0]!["name"]!.GetValue<string>().ShouldBe("Issues");
        var issue = parameters[0]!["resource"]!["issue"]![0]!;
        issue["severity"]!.GetValue<string>().ShouldBe("information");
        issue["code"]!.GetValue<string>().ShouldBe("informational");
        issue["diagnostics"]!.GetValue<string>().ShouldBe("Job In Progress");
    }

    [Fact]
    public void GivenQueuedJob_WhenBuilding_ThenResponseIsAlso202()
    {
        var result = new GetBulkDeleteStatusResult("Queued", NoCounts, [], null);

        var response = BulkDeleteStatusResponseBuilder.Build(result, FhirVersion.R4);

        response.StatusCode.ShouldBe(202);
    }

    [Fact]
    public void GivenRunningJobWithPartialCounts_WhenBuilding_ThenResourceDeletedCountFollowsIssues()
    {
        var counts = new Dictionary<string, long> { ["Patient"] = 2 };
        var result = new GetBulkDeleteStatusResult("Running", counts, [], null);

        var response = BulkDeleteStatusResponseBuilder.Build(result, FhirVersion.R4);

        var parameters = response.Body["parameter"]!.AsArray();
        parameters.Count.ShouldBe(2);
        parameters[0]!["name"]!.GetValue<string>().ShouldBe("Issues");
        parameters[1]!["name"]!.GetValue<string>().ShouldBe("ResourceDeletedCount");
    }

    [Fact]
    public void GivenCompletedJobWithNoDeletions_WhenBuilding_ThenParameterArrayIsOmitted()
    {
        var result = new GetBulkDeleteStatusResult("Completed", NoCounts, [], null);

        var response = BulkDeleteStatusResponseBuilder.Build(result, FhirVersion.R4);

        response.StatusCode.ShouldBe(200);
        response.Body.ContainsKey("parameter").ShouldBeFalse();
    }

    [Fact]
    public void GivenCompletedJobWithCounts_WhenBuildingForR4_ThenValueInteger64IsAJsonString()
    {
        // GetBulkDeleteStatusResult.ResourceDeletedCount already contains only positive counts (the
        // upstream handler filters zero-count types out); the builder trusts that invariant.
        var counts = new Dictionary<string, long> { ["Patient"] = 2 };
        var result = new GetBulkDeleteStatusResult("Completed", counts, [], null);

        var response = BulkDeleteStatusResponseBuilder.Build(result, FhirVersion.R4);

        var parts = response.Body["parameter"]![0]!["part"]!.AsArray();
        parts.Count.ShouldBe(1);
        parts[0]!["name"]!.GetValue<string>().ShouldBe("Patient");
        var valueNode = parts[0]!["valueInteger64"]!;
        valueNode.GetValueKind().ShouldBe(System.Text.Json.JsonValueKind.String);
        valueNode.GetValue<string>().ShouldBe("2");
    }

    [Fact]
    public void GivenCompletedJobWithCounts_WhenBuildingForStu3_ThenValueDecimalIsAJsonNumber()
    {
        var counts = new Dictionary<string, long> { ["Patient"] = 2 };
        var result = new GetBulkDeleteStatusResult("Completed", counts, [], null);

        var response = BulkDeleteStatusResponseBuilder.Build(result, FhirVersion.Stu3);

        var part = response.Body["parameter"]![0]!["part"]![0]!;
        part.AsObject().ContainsKey("valueInteger64").ShouldBeFalse();
        var valueNode = part["valueDecimal"]!;
        valueNode.GetValueKind().ShouldBe(System.Text.Json.JsonValueKind.Number);
        valueNode.GetValue<long>().ShouldBe(2);
    }

    [Fact]
    public void GivenCancelledJob_WhenBuilding_ThenResponseIs200WithWarningIssue()
    {
        var counts = new Dictionary<string, long> { ["Patient"] = 1 };
        var result = new GetBulkDeleteStatusResult("Cancelled", counts, [], null);

        var response = BulkDeleteStatusResponseBuilder.Build(result, FhirVersion.R4);

        response.StatusCode.ShouldBe(200);
        var issue = response.Body["parameter"]![0]!["resource"]!["issue"]![0]!;
        issue["severity"]!.GetValue<string>().ShouldBe("warning");
        issue["code"]!.GetValue<string>().ShouldBe("informational");
        issue["diagnostics"]!.GetValue<string>().ShouldBe("Job Canceled");
        response.Body["parameter"]![1]!["name"]!.GetValue<string>().ShouldBe("ResourceDeletedCount");
    }

    [Fact]
    public void GivenFailedJobWithErrorMessage_WhenBuilding_ThenResponseIs500WithErrorIssueFromErrorMessage()
    {
        var result = new GetBulkDeleteStatusResult("Failed", NoCounts, ["some persisted issue"], "boom");

        var response = BulkDeleteStatusResponseBuilder.Build(result, FhirVersion.R4);

        response.StatusCode.ShouldBe(500);
        var issues = response.Body["parameter"]![0]!["resource"]!["issue"]!.AsArray();
        issues.Count.ShouldBe(1);
        issues[0]!["severity"]!.GetValue<string>().ShouldBe("error");
        issues[0]!["code"]!.GetValue<string>().ShouldBe("exception");
        issues[0]!["diagnostics"]!.GetValue<string>().ShouldBe("boom");
    }

    [Fact]
    public void GivenFailedJobWithOnlyIssuesList_WhenBuilding_ThenEachIssueBecomesAnOperationOutcomeIssue()
    {
        var result = new GetBulkDeleteStatusResult("Failed", NoCounts, ["issue one", "issue two"], null);

        var response = BulkDeleteStatusResponseBuilder.Build(result, FhirVersion.R4);

        var issues = response.Body["parameter"]![0]!["resource"]!["issue"]!.AsArray();
        issues.Count.ShouldBe(2);
        issues[0]!["diagnostics"]!.GetValue<string>().ShouldBe("issue one");
        issues[1]!["diagnostics"]!.GetValue<string>().ShouldBe("issue two");
    }
}
