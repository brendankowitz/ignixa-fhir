using Ignixa.Serialization.Abstractions;
using Ignixa.Models;

namespace Ignixa.Application.Features.Bundle;

/// <summary>
/// The exception that is thrown when an entry of a transaction bundle fails, rejecting the whole transaction.
/// </summary>
/// <remarks>
/// The summary issue comes first, followed by the failing entry's own issues so clients can see why it failed.
/// The summary issue's code follows the entry's status rather than always reporting a malformed request.
/// </remarks>
public sealed class BundleTransactionException : FhirException
{
    private readonly int _statusCode;

    /// <param name="message">Summary of which entry failed.</param>
    /// <param name="statusCode">The failing entry's status (400 or above), returned as the transaction response status.</param>
    /// <param name="entryIssues">The failing entry's OperationOutcome issues, added after the summary issue.</param>
    public BundleTransactionException(string message, int statusCode, IEnumerable<OperationOutcomeIssue>? entryIssues = null)
        : base(message)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(statusCode, 400);
        _statusCode = statusCode;
        Issues.Add(new OperationOutcomeIssue
        {
            SeverityCode = OperationOutcomeIssue.IssueSeverityCode.Error,
            IssueTypeCode = SummaryIssueCode(statusCode),
            Diagnostics = message,
        });
        foreach (var issue in entryIssues ?? [])
        {
            Issues.Add(issue);
        }
    }

    public override int StatusCode => _statusCode;

    private static OperationOutcomeIssue.IssueTypeCommon SummaryIssueCode(int statusCode) => statusCode switch
    {
        404 => OperationOutcomeIssue.IssueTypeCommon.NotFound,
        409 => OperationOutcomeIssue.IssueTypeCommon.Conflict,
        412 => OperationOutcomeIssue.IssueTypeCommon.Required,
        >= 500 => OperationOutcomeIssue.IssueTypeCommon.Exception,
        _ => OperationOutcomeIssue.IssueTypeCommon.Invalid,
    };
}
