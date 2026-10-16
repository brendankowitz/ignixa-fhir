// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.RegularExpressions;
using Ignixa.CliOnFhir.Catalog;
using Ignixa.CliOnFhir.Terminology;
using Ignixa.Validation;

namespace Ignixa.CliOnFhir.Validation;

/// <summary>
/// Names each validator issue by rule and says whether its check concluded. The validator reports a check it could not run
/// as a non-failing warning or information (an unloaded profile, an unevaluable constraint, a deferred slicing, a binding
/// the terminology adapter could not check); those get a <see cref="ValidationRules"/> name so the verdict can be
/// <c>unknown</c> rather than <c>valid</c>. The validator's messages interpolate data (values, property names) and profile
/// text (constraint <c>human</c>, patterns), so they are only read to classify, never carried: what is kept is the issue code,
/// a constraint key and a ValueSet canonical, each shape-checked, and the path with data-chosen names as digests.
/// </summary>
internal static partial class IssueClassifier
{
    /// <param name="issue">The validator's (or a detector's) issue.</param>
    /// <param name="profile">The StructureDefinition whose schema raised it, already shape-checked.</param>
    public static ClassifiedIssue Classify(ValidationIssue issue, string profile)
    {
        ArgumentNullException.ThrowIfNull(issue);

        string severity = issue.Severity switch
        {
            IssueSeverity.Fatal => "fatal",
            IssueSeverity.Error => "error",
            IssueSeverity.Warning => "warning",
            _ => "information",
        };

        (string rule, string? constraint, string? valueSet) = Rule(issue);
        return new ClassifiedIssue(severity, profile, rule, constraint, valueSet, Path(rule, issue.Path));
    }

    private static (string Rule, string? Constraint, string? ValueSet) Rule(ValidationIssue issue)
    {
        // Keyed on the validator's template, whose only argument is the binding's ValueSet canonical (definition content).
        if (issue.TemplateId == IssueTemplates.BindingRequiredTextOnly.Id && issue.TemplateArguments is [var valueSet])
        {
            (string url, string? version) = TerminologyStore.SplitCanonical(valueSet);
            return (ValidationRules.BindingRequiredTextOnly, null, TerminologyNames.Canonical(url, version));
        }

        // The terminology adapter starts each of its messages with its rule name and names only the (shape-checked) ValueSet.
        if (issue.Code is "code-invalid" or "terminology-validation-warning" or "terminology-unavailable"
            && StoreTerminologyService.Rules.FirstOrDefault(rule => issue.Message.StartsWith(rule + ":", StringComparison.Ordinal)) is { } adapterRule)
        {
            return (adapterRule, null, AdapterValueSet().Match(issue.Message) is { Success: true } match ? match.Groups["vs"].Value : null);
        }

        if (issue.Code is "terminology-error" or "terminology-unavailable")
        {
            return (ValidationRules.TerminologyError, null, null);
        }

        if (issue.Code == "unresolvable-profile")
        {
            return (ValidationRules.ProfileNotLoaded, null, null);
        }

        // FhirPathInvariantCheck: "Constraint '<key>' could not be evaluated: <engine message>".
        if (NotEvaluated().Match(issue.Message) is { Success: true } notEvaluated && notEvaluated.Groups["key"].Value == issue.Code)
        {
            return (ValidationRules.ConstraintNotEvaluated, Token(issue.Code), null);
        }

        // A failed invariant is reported as "<key>: <human>", its code the key; the validator's own codes are in the table.
        if (!RuleDescriptions.IsKnownRule(issue.Code) && ConstraintKey().IsMatch(issue.Code) && issue.Message.StartsWith(issue.Code + ":", StringComparison.Ordinal))
        {
            return (ValidationRules.Invariant, Token(issue.Code), null);
        }

        return (Token(issue.Code), null, null);
    }

    /// <summary>The path without indexes; an unknown property's name is the data's own, so it is always a digest.</summary>
    private static string Path(string rule, string path)
    {
        string normalized = IssuePath.Normalize(path);
        if (rule != "unknown-property")
        {
            return normalized;
        }

        int dot = normalized.LastIndexOf('.');
        string last = path[(path.LastIndexOf('.') + 1)..];
        return dot < 0 ? FhirIds.Digest(last) : $"{normalized[..dot]}.{(FhirIds.IsDigest(normalized[(dot + 1)..]) ? normalized[(dot + 1)..] : FhirIds.Digest(last))}";
    }

    private static string Token(string code) => TerminologyNames.Token(code);

    [GeneratedRegex("^Constraint '(?<key>[^']+)' could not be evaluated")]
    private static partial Regex NotEvaluated();

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9]*(-[A-Za-z0-9]+)*-[0-9]+[a-z]?$")]
    private static partial Regex ConstraintKey();

    [GeneratedRegex("ValueSet (?<vs>[^\\s;()]+)")]
    private static partial Regex AdapterValueSet();
}
