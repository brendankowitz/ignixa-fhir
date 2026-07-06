// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Validation.Tests.Conformance;

/// <summary>
/// Why an in-scope (R4 clean-base) manifest entry was excluded from the conformance sample because
/// its reference outcome could not be resolved. Tracked explicitly so a shrinking or biased sample
/// is visible in the report instead of silently shrinking the denominator.
/// </summary>
public enum ConformanceSkipReason
{
    /// <summary>The manifest entry has no <c>java</c> property at all.</summary>
    NoOutcomeField,

    /// <summary>The <c>java</c> property is a string path, but no file exists at that path under <c>outcomes/</c>.</summary>
    OutcomeFileMissing,

    /// <summary>The outcome file exists but is not parseable JSON.</summary>
    OutcomeFileMalformed,

    /// <summary>The <c>java</c> value (inline object or parsed outcome file) does not match any shape this loader recognizes.</summary>
    UnrecognizedOutcomeShape,
}
