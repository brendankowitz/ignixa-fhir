using Ignixa.Models;
using Ignixa.Serialization.Abstractions;
using Ignixa.Serialization.Models;

namespace Ignixa.Application.Features.Conformance;

/// <summary>
/// Thrown when a request attempts to evaluate search parameters after the local conformance lease expired.
/// </summary>
public sealed class ConformanceStaleException : FhirException
{
    public const string MessageText = "Conformance state is stale; search is temporarily unavailable.";
    public const string DefinitionsChangedMessageText =
        "Conformance definitions changed while the resource was being written; retry the request.";

    public ConformanceStaleException(TimeSpan retryAfter)
        : base(MessageText, CreateIssue(MessageText))
    {
        RetryAfter = retryAfter;
    }

    public ConformanceStaleException(
        TimeSpan retryAfter,
        Exception innerException)
        : base(DefinitionsChangedMessageText, innerException, CreateIssue(DefinitionsChangedMessageText))
    {
        RetryAfter = retryAfter;
    }

    public override int StatusCode => 503;

    public TimeSpan RetryAfter { get; }

    private static OperationOutcomeIssue CreateIssue(string diagnostics) =>
        new()
        {
            SeverityCode = OperationOutcomeIssue.IssueSeverityCode.Error,
            IssueTypeCode = OperationOutcomeIssue.IssueTypeCommon.Transient,
            Diagnostics = diagnostics,
        };
}
