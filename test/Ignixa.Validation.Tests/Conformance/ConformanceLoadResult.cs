// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Validation.Tests.Conformance;

/// <summary>
/// Result of loading the R4 clean-base slice of the vendored validator manifest: the cases that
/// resolved to a usable Java reference outcome, plus every in-scope case that was skipped (and why).
/// </summary>
/// <param name="Cases">Resolved cases paired with their expected outcome.</param>
/// <param name="Skips">In-scope cases excluded because their Java outcome could not be resolved.</param>
public sealed record ConformanceLoadResult(
    IReadOnlyList<(ConformanceTestCase Case, ConformanceExpectation Expected)> Cases,
    IReadOnlyList<ConformanceSkip> Skips);
