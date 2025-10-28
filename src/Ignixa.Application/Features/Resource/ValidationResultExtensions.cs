// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Serialization.Models;
using Ignixa.Validation;

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
    public static OperationOutcomeJsonNode ToOperationOutcome(this ValidationResult validationResult)
    {
        ArgumentNullException.ThrowIfNull(validationResult);

        var outcome = new OperationOutcomeJsonNode();
        var issueList = new List<OperationOutcomeJsonNode.IssueComponent>();

        foreach (var issue in validationResult.Issues)
        {
            var issueComponent = new OperationOutcomeJsonNode.IssueComponent
            {
                Severity = MapSeverity(issue.Severity),
                Code = OperationOutcomeJsonNode.IssueType.Invalid,
                Diagnostics = issue.Message
            };
            issueComponent.AddExpression(issue.Path);
            issueList.Add(issueComponent);
        }

        // If no issues, add a success message
        if (issueList.Count == 0)
        {
            issueList.Add(new OperationOutcomeJsonNode.IssueComponent
            {
                Severity = OperationOutcomeJsonNode.IssueSeverity.Information,
                Code = OperationOutcomeJsonNode.IssueType.Informational,
                Diagnostics = "Validation passed with no issues"
            });
        }

        outcome.SetIssues(issueList);
        return outcome;
    }

    /// <summary>
    /// Maps our internal IssueSeverity to FHIR OperationOutcome.IssueSeverity.
    /// </summary>
    private static OperationOutcomeJsonNode.IssueSeverity MapSeverity(IssueSeverity severity)
    {
        return severity switch
        {
            IssueSeverity.Information => OperationOutcomeJsonNode.IssueSeverity.Information,
            IssueSeverity.Warning => OperationOutcomeJsonNode.IssueSeverity.Warning,
            IssueSeverity.Error => OperationOutcomeJsonNode.IssueSeverity.Error,
            IssueSeverity.Fatal => OperationOutcomeJsonNode.IssueSeverity.Fatal,
            _ => OperationOutcomeJsonNode.IssueSeverity.Error
        };
    }
}
