// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Catalog;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.Validation;

namespace Ignixa.CliOnFhir.Validation;

/// <summary>One resource validated, or found unreadable.</summary>
/// <param name="Entry">The resource.</param>
/// <param name="Verdict"><c>valid</c>, <c>invalid</c>, <c>unknown</c>, or <c>unreadable</c> with <paramref name="Problem"/>.</param>
/// <param name="Issues">Its issues as the summary groups them, in the validator's order.</param>
/// <param name="Raw">The validator's own issues, in the same order, for the full OperationOutcome; empty when unreadable.</param>
/// <param name="Problem">Why it could not be read; null otherwise.</param>
internal sealed record ResourceValidation(CatalogEntry Entry, string Verdict, IReadOnlyList<ClassifiedIssue> Issues, IReadOnlyList<ValidationIssue> Raw, IssueItem? Problem)
{
    public const string Valid = "valid";
    public const string Invalid = "invalid";
    public const string Unknown = "unknown";
    public const string Unreadable = "unreadable";

    /// <summary>References whose target type could not be told, so their target type was not checked.</summary>
    public int UndeterminableReferences { get; init; }

    /// <summary>References with only a display, which name no target to check.</summary>
    public int DisplayOnlyReferences { get; init; }

    /// <summary>The verdict the issues give: invalid on any failure, unknown when a check did not conclude, otherwise valid.</summary>
    public static string VerdictOf(IReadOnlyList<ClassifiedIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(issues);

        return issues.Any(issue => issue.Fails) ? Invalid : issues.Any(issue => issue.Unevaluated) ? Unknown : Valid;
    }
}
