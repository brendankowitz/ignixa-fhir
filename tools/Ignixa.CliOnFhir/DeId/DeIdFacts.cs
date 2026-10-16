// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Validation;

namespace Ignixa.CliOnFhir.DeId;

/// <summary>What a de-identification run did: counts, the rules by method, and paths; never a value, original or de-identified.</summary>
/// <param name="Scope">Which resources were in scope.</param>
/// <param name="Config">The configuration applied, by shape.</param>
/// <param name="Out">The directory written into (absolute).</param>
/// <param name="Types">Per resource type in scope, in the order first met.</param>
/// <param name="Operations">
/// Elements changed over the resources written, as <c>Ignixa.DeId</c> counts them: per operation, not per rule, and by its own labels, so
/// a <c>dateShift</c> is counted as <c>perturb</c> and a value redacted in part (a date cut to its year) as <c>abstract</c>.
/// </param>
/// <param name="Processed">Resources in scope.</param>
/// <param name="Written">Resources written.</param>
/// <param name="Failed">Resources not written.</param>
/// <param name="ReferencesResolved">References in the resources written that named a Bundle entry or a conditional match and were written as its <c>Type/id</c>.</param>
public sealed record DeIdFacts(
    ValidationScopeFacts Scope,
    DeIdConfigFacts Config,
    string Out,
    IReadOnlyList<DeIdTypeFacts> Types,
    IReadOnlyDictionary<string, long> Operations,
    int Processed,
    int Written,
    int Failed,
    int ReferencesResolved);
