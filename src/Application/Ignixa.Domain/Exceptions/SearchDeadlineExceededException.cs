using Ignixa.Models;
using Ignixa.Serialization.Abstractions;
using Ignixa.Serialization.Models;

namespace Ignixa.Domain.Exceptions;

/// <summary>
/// A request-originated search ran past the deadline it was admitted with. Finishing it could evaluate outgoing
/// search-parameter definitions after a transition committed, so it fails closed with a retryable 503.
/// </summary>
public sealed class SearchDeadlineExceededException(Exception innerException)
    : FhirException(MessageText, innerException, CreateIssue())
{
    private const string MessageText = "The search did not finish within its execution deadline; retry the request.";

    public override int StatusCode => 503;

    private static OperationOutcomeIssue CreateIssue() =>
        new()
        {
            SeverityCode = OperationOutcomeIssue.IssueSeverityCode.Error,
            IssueTypeCode = OperationOutcomeIssue.IssueTypeCommon.Transient,
            Diagnostics = MessageText,
        };
}
