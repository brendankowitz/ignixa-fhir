using Ignixa.Application.Features.Conformance;
using Shouldly;

namespace Ignixa.Application.Tests.Features.Conformance;

public class ValidationIssueTests
{
    // §4.4, §7: the activation response carries these as FHIR issue-severity codes.
    [Fact]
    public void GivenEachSeverity_WhenItsCodeIsRead_ThenItIsTheFhirIssueSeverityCode()
    {
        new ValidationIssue("SP_CONFLICT", "conflict").SeverityCode.ShouldBe("error");
        ValidationIssue.Warning("REINDEX_DEFERRED", "deferred").SeverityCode.ShouldBe("warning");
        ValidationIssue.Information("REINDEX_STARTED", "started").SeverityCode.ShouldBe("information");
    }
}
