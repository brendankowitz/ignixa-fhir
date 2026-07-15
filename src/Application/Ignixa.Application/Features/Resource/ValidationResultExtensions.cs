// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Models;
using Ignixa.Serialization.Models;
using Ignixa.Validation;
using System.Text.Json.Nodes;

namespace Ignixa.Application.Features.Resource;

/// <summary>
/// Extension methods for converting validation results to FHIR OperationOutcome.
/// </summary>
public static class ValidationResultExtensions
{
    /// <summary>
    /// Converts a ValidationResult to a FHIR OperationOutcome resource.
    /// </summary>
    /// <param name="validationResult">The validation result to convert.</param>
    /// <returns>An OperationOutcome resource with issues from the validation result.</returns>
    public static OperationOutcome ToOperationOutcome(this ValidationResult validationResult)
    {
        ArgumentNullException.ThrowIfNull(validationResult);

        var outcome = new OperationOutcome();
        var issueList = new List<OperationOutcomeIssue>();

        foreach (var issue in validationResult.Issues)
        {
            var issueComponent = new OperationOutcomeIssue()
            {
                SeverityCode = MapSeverity(issue.Severity),
                IssueTypeCode = OperationOutcomeIssue.IssueTypeCommon.Invalid,
                Diagnostics = issue.Message
            };
            issueComponent.Expression.Add(issue.Path);
            issueList.Add(issueComponent);
        }

        // If no issues, add a success message
        if (issueList.Count == 0)
        {
            issueList.Add(new OperationOutcomeIssue()
            {
                SeverityCode = OperationOutcomeIssue.IssueSeverityCode.Information,
                IssueTypeCode = OperationOutcomeIssue.IssueTypeCommon.Informational,
                Diagnostics = "Validation passed with no issues"
            });
        }

        foreach (var item in issueList)
        {
            outcome.Issue.Add(item);
        }
        return outcome;
    }

    /// <summary>
    /// Maps our internal IssueSeverity to FHIR OperationOutcome.IssueSeverity.
    /// </summary>
    private static OperationOutcomeIssue.IssueSeverityCode MapSeverity(IssueSeverity severity)
    {
        return severity switch
        {
            IssueSeverity.Information => OperationOutcomeIssue.IssueSeverityCode.Information,
            IssueSeverity.Warning => OperationOutcomeIssue.IssueSeverityCode.Warning,
            IssueSeverity.Error => OperationOutcomeIssue.IssueSeverityCode.Error,
            IssueSeverity.Fatal => OperationOutcomeIssue.IssueSeverityCode.Fatal,
            _ => OperationOutcomeIssue.IssueSeverityCode.Error
        };
    }
}
