// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;

namespace Ignixa.Validation.Abstractions;

/// <summary>
/// Marks a profile-tier <see cref="IValidationCheck"/> that must also run at
/// <see cref="ValidationDepth.Compatibility"/> depth, in addition to <see cref="ValidationDepth.Full"/>.
/// <para>
/// Reserved for closed-world, terminology-independent conformance checks (e.g. CodeSystem/ValueSet
/// shape rules) that Microsoft FHIR Server's Firely-based fallback validator also enforces at
/// Compatibility depth. Every other profile check (FHIRPath invariants, slicing, reference resolution,
/// choice-variant recursion) intentionally stays Full-only — see <c>ValidationDepthTests</c> for the
/// regression coverage protecting that boundary (Bug #210-6: a blanket depth comparison previously let
/// Compatibility run the entire profile tier).
/// </para>
/// </summary>
[SuppressMessage("Design", "CA1040:Avoid empty interfaces", Justification = "Marker interface: carries the run-at-Compatibility-too contract for ValidationSchema.Validate.")]
public interface ICompatibilityConformanceCheck
{
}
