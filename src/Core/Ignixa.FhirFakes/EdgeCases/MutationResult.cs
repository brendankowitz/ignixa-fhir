// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.FhirFakes.EdgeCases;

/// <summary>
/// The outcome of applying an <see cref="IEdgeCaseStrategy"/> to a <see cref="MutationTarget"/>:
/// the new value written and a short human-readable description of what changed.
/// </summary>
/// <param name="NewValue">The value the strategy wrote to the target.</param>
/// <param name="Description">A short description of the mutation (used for the manifest record).</param>
public sealed record MutationResult(string NewValue, string Description);
