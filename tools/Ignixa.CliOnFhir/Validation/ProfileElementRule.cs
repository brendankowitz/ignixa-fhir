// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Validation;

/// <summary>What a profile's element definition (snapshot, then differential) says that the tool checks itself.</summary>
/// <param name="RequiredValueSet">The ValueSet of a <c>required</c> binding on the element; null when it has none.</param>
/// <param name="TargetProfiles">The <c>targetProfile</c> canonicals of its <c>Reference</c> type; null when it names none.</param>
internal sealed record ProfileElementRule(string? RequiredValueSet, IReadOnlyList<string>? TargetProfiles);
