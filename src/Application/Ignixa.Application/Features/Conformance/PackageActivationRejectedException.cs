using Ignixa.Models;
using Ignixa.Serialization.Abstractions;
using Ignixa.Serialization.Models;

namespace Ignixa.Application.Features.Conformance;

/// <summary>
/// Thrown when a stored package could not be activated, so none of its definitions changed.
/// </summary>
/// <remarks>
/// A lost append race (<c>CONFORMANCE_CONFLICT</c>) is 409 and can be retried as is; every other activation issue
/// is a business-rule rejection of the package content against the current conformance state, 422. Each
/// OperationOutcome issue names its activation issue code in <c>details.coding</c>.
/// </remarks>
public sealed class PackageActivationRejectedException : FhirException
{
    public const string ConformanceConflictCode = "CONFORMANCE_CONFLICT";
    public const string IssueCodeSystem = "urn:ignixa:package-activation-issue";

    public PackageActivationRejectedException(string packageId, string version, IReadOnlyList<ValidationIssue> issues)
        : base(
            $"Package {packageId}@{version} was stored but not activated: " +
            string.Join("; ", issues.Select(issue => $"{issue.Code}: {issue.Message}")),
            issues.Select(issue => CreateIssue(packageId, version, issue)).ToArray())
    {
        ActivationIssues = issues;
    }

    public IReadOnlyList<ValidationIssue> ActivationIssues { get; }

    public override int StatusCode =>
        ActivationIssues.Any(issue => issue.Code == ConformanceConflictCode) ? 409 : 422;

    private static OperationOutcomeIssue CreateIssue(string packageId, string version, ValidationIssue issue)
    {
        var details = new CodeableConcept { Text = $"Package {packageId}@{version} was stored but not activated." };
        details.Coding.Add(new Coding { System = IssueCodeSystem, Code = issue.Code });
        return new OperationOutcomeIssue
        {
            SeverityCode = OperationOutcomeIssue.IssueSeverityCode.Error,
            IssueTypeCode = issue.Code == ConformanceConflictCode
                ? OperationOutcomeIssue.IssueTypeCommon.Conflict
                : OperationOutcomeIssue.IssueTypeCommon.BusinessRule,
            Details = details,
            Diagnostics = issue.Message,
        };
    }
}
