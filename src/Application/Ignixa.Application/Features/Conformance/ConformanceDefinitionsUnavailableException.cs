using Ignixa.Models;
using Ignixa.Serialization.Abstractions;
using Ignixa.Serialization.Models;

namespace Ignixa.Application.Features.Conformance;

public sealed class ConformanceDefinitionsUnavailableException : FhirException
{
    public const string MessageText =
        "Conformance definitions changed while the resource was being written; retry the request.";

    public ConformanceDefinitionsUnavailableException(TimeSpan retryAfter, Exception innerException)
        : base(MessageText, innerException, CreateIssue())
    {
        RetryAfter = retryAfter;
    }

    public override int StatusCode => 503;

    public TimeSpan RetryAfter { get; }

    private static OperationOutcomeIssue CreateIssue() =>
        new()
        {
            SeverityCode = OperationOutcomeIssue.IssueSeverityCode.Error,
            IssueTypeCode = OperationOutcomeIssue.IssueTypeCommon.Transient,
            Diagnostics = MessageText,
        };
}
