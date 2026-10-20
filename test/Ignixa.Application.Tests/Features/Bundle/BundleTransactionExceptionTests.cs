using Ignixa.Application.Features.Bundle;
using Ignixa.Models;
using Shouldly;
using Xunit;

namespace Ignixa.Application.Tests.Features.Bundle;

public class BundleTransactionExceptionTests
{
    [Theory]
    [InlineData(400, OperationOutcomeIssue.IssueTypeCommon.Invalid)]
    [InlineData(404, OperationOutcomeIssue.IssueTypeCommon.NotFound)]
    [InlineData(409, OperationOutcomeIssue.IssueTypeCommon.Conflict)]
    [InlineData(412, OperationOutcomeIssue.IssueTypeCommon.Required)]
    [InlineData(500, OperationOutcomeIssue.IssueTypeCommon.Exception)]
    public void GivenFailingEntryStatus_WhenCreated_ThenSummaryIssueCodeFollowsTheStatus(
        int statusCode, OperationOutcomeIssue.IssueTypeCommon expected)
    {
        // Act
        var exception = new BundleTransactionException("entry failed", statusCode);

        // Assert
        exception.StatusCode.ShouldBe(statusCode);
        exception.Issues.ShouldHaveSingleItem().IssueTypeCode.ShouldBe(expected);
    }

    [Fact]
    public void GivenEntryIssues_WhenCreated_ThenSummaryIssueComesFirst()
    {
        // Arrange
        var entryIssue = new OperationOutcomeIssue { Diagnostics = "cause" };

        // Act
        var exception = new BundleTransactionException("entry failed", 412, [entryIssue]);

        // Assert
        exception.Issues.Select(i => i.Diagnostics).ShouldBe(["entry failed", "cause"]);
    }

    [Fact]
    public void GivenSuccessStatus_WhenCreated_ThenItIsRejected()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new BundleTransactionException("entry failed", 200));
    }
}
