// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Validation.Tests.Conformance;

/// <summary>
/// An R4 clean-base manifest entry that was excluded from the conformance sample, and why.
/// </summary>
/// <param name="Name">The manifest entry's name (or file name when unnamed).</param>
/// <param name="Reason">Why the entry's Java reference outcome could not be resolved.</param>
public sealed record ConformanceSkip(string Name, ConformanceSkipReason Reason);
