// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.Model;
using Sparky.Validation;

namespace Sparky.Application.Features.Resource;

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

        var outcome = new OperationOutcome
        {
            Issue = new List<OperationOutcome.IssueComponent>()
        };

        foreach (var issue in validationResult.Issues)
        {
            outcome.Issue.Add(new OperationOutcome.IssueComponent
            {
                Severity = MapSeverity(issue.Severity),
                Code = OperationOutcome.IssueType.Invalid,
                Diagnostics = issue.Message,
                Expression = new List<string> { issue.Path }
            });
        }

        // If no issues, add a success message
        if (outcome.Issue.Count == 0)
        {
            outcome.Issue.Add(new OperationOutcome.IssueComponent
            {
                Severity = OperationOutcome.IssueSeverity.Information,
                Code = OperationOutcome.IssueType.Informational,
                Diagnostics = "Validation passed with no issues"
            });
        }

        return outcome;
    }

    /// <summary>
    /// Maps our internal IssueSeverity to FHIR OperationOutcome.IssueSeverity.
    /// </summary>
    private static OperationOutcome.IssueSeverity MapSeverity(IssueSeverity severity)
    {
        return severity switch
        {
            IssueSeverity.Information => OperationOutcome.IssueSeverity.Information,
            IssueSeverity.Warning => OperationOutcome.IssueSeverity.Warning,
            IssueSeverity.Error => OperationOutcome.IssueSeverity.Error,
            IssueSeverity.Fatal => OperationOutcome.IssueSeverity.Fatal,
            _ => OperationOutcome.IssueSeverity.Error
        };
    }
}
