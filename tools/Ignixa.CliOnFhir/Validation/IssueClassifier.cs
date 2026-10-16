// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.RegularExpressions;
using Ignixa.CliOnFhir.Terminology;
using Ignixa.Validation;

namespace Ignixa.CliOnFhir.Validation;

/// <summary>
/// Names each validator issue by rule and says whether its check concluded. The validator reports a check it could not run
/// as a non-failing warning or information (an unloaded profile, an unevaluable constraint, a deferred slicing, a binding
/// the terminology adapter could not check); those get a <see cref="ValidationRules"/> name so the verdict can be
/// <c>unknown</c> rather than <c>valid</c>.
/// </summary>
internal static partial class IssueClassifier
{
    /// <param name="issue">The validator's issue.</param>
    /// <param name="profile">The StructureDefinition whose schema raised it, already shape-checked.</param>
    /// <param name="vocabulary">The resource's string values (<see cref="MessageTemplate.Vocabulary"/>).</param>
    public static ClassifiedIssue Classify(ValidationIssue issue, string profile, IReadOnlyList<string> vocabulary)
    {
        ArgumentNullException.ThrowIfNull(issue);

        string severity = issue.Severity switch
        {
            IssueSeverity.Fatal => "fatal",
            IssueSeverity.Error => "error",
            IssueSeverity.Warning => "warning",
            _ => "information",
        };

        (string rule, string message) = Rule(issue);
        return new ClassifiedIssue(severity, profile, rule, IssuePath.Normalize(issue.Path), MessageTemplate.Of(message, vocabulary));
    }

    private static (string Rule, string Message) Rule(ValidationIssue issue)
    {
        // The terminology adapter starts each of its messages with its rule name; BindingCheck wraps them in its own codes.
        if (issue.Code is "code-invalid" or "terminology-validation-warning" or "terminology-unavailable"
            && StoreTerminologyService.Rules.FirstOrDefault(rule => issue.Message.StartsWith(rule + ":", StringComparison.Ordinal)) is { } adapterRule)
        {
            return (adapterRule, issue.Message);
        }

        if (issue.Code is "terminology-error" or "terminology-unavailable")
        {
            // BindingCheck's own failure text quotes an exception message, which may quote the data.
            return (ValidationRules.TerminologyError, "the terminology adapter failed, so the binding was not checked");
        }

        if (issue.Code == "unresolvable-profile")
        {
            return (ValidationRules.ProfileNotLoaded, "a profile in meta.profile is not loaded and was not validated");
        }

        if (issue.Code is "slicing-deferred" or "validation-nesting-limit")
        {
            return (issue.Code == "slicing-deferred" ? ValidationRules.SlicingDeferred : ValidationRules.NestingLimit, issue.Message);
        }

        // FhirPathInvariantCheck: "Constraint '<key>' could not be evaluated: <engine message>"; the engine message is dropped.
        if (NotEvaluated().Match(issue.Message) is { Success: true } notEvaluated && notEvaluated.Groups["key"].Value == issue.Code)
        {
            return (ValidationRules.ConstraintNotEvaluated, $"constraint {Token(issue.Code)} could not be evaluated");
        }

        return (Token(issue.Code), issue.Message);
    }

    private static string Token(string code) => TerminologyNames.Token(code);

    [GeneratedRegex("^Constraint '(?<key>[^']+)' could not be evaluated")]
    private static partial Regex NotEvaluated();
}
