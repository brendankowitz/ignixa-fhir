// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Drafting;

/// <summary>One check of a draft.</summary>
/// <param name="Check"><c>validation</c>, <c>terminology</c> or <c>reference</c>.</param>
/// <param name="Element">What was checked: the resource type for validation, else the element's path.</param>
/// <param name="Outcome"><c>pass</c>, <c>warning</c> (it passes, with a problem worth fixing), <c>fail</c>, or <c>unknown</c> (it could not be evaluated).</param>
/// <param name="Details">The check's own facts: <see cref="ValidationCheckDetails"/>, <see cref="TerminologyCheckDetails"/> or <see cref="ReferenceCheckDetails"/>.</param>
public sealed record DraftCheck(string Check, string Element, string Outcome, object Details)
{
    public const string Pass = "pass";
    public const string Warning = "warning";
    public const string Fail = "fail";
    public const string Unknown = "unknown";
}
