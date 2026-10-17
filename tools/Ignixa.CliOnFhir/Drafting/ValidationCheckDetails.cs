// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Drafting;

/// <summary>What the validator (the one <c>validate</c> runs, at depth full) said about a draft.</summary>
/// <param name="Verdict"><c>valid</c>, <c>invalid</c> or <c>unknown</c> (a check did not conclude: a binding the loaded terminology could not decide, a constraint that could not be evaluated, ...).</param>
/// <param name="Profile">The profile it was validated against besides the core definition; null for the core definition only.</param>
/// <param name="Packages">The packages whose StructureDefinitions were loaded.</param>
/// <param name="Issues">The validator's issues, one per rule and path, in its order; never its messages, which quote the resource.</param>
/// <param name="IssuesNotListed">Issues beyond the first <see cref="DraftValidation.MaxIssues"/>, counted.</param>
public sealed record ValidationCheckDetails(string Verdict, string? Profile, IReadOnlyList<string> Packages, IReadOnlyList<DraftIssueGroup> Issues, int IssuesNotListed);
