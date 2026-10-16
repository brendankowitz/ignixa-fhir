// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Cohorts;

/// <summary>A patient has a Condition with one of the codes (listed, or of the ValueSet the folder uses).</summary>
/// <param name="Codes">Codes listed by the request.</param>
/// <param name="ValueSet">A ValueSet url (or <c>isa/&lt;sctid&gt;</c>) whose codes the folder uses are added to <paramref name="Codes"/>.</param>
/// <param name="ActiveOnly">Only a Condition active at <c>asOf</c> counts; otherwise any (not entered in error).</param>
/// <param name="ConfirmedOnly">Only a Condition whose verification status is confirmed or absent counts; otherwise any but entered-in-error.</param>
internal sealed record ConditionCriterion(IReadOnlyList<CohortCode> Codes, string? ValueSet, bool ActiveOnly, bool ConfirmedOnly);
