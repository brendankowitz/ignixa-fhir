// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Validation;

/// <summary>
/// The references of one resource whose target type the validator reported it could not check
/// (<see cref="ValidationRules.ReferenceTargetUnchecked"/>), by element path, so a reference two schemas report is counted once.
/// For one resource; not thread-safe.
/// </summary>
internal sealed class UncheckedReferences
{
    /// <summary>References with only a display.</summary>
    public HashSet<string> DisplayOnly { get; } = new(StringComparer.Ordinal);

    /// <summary>References whose target type could not be told, or whose element's target profile is not loaded.</summary>
    public HashSet<string> Undeterminable { get; } = new(StringComparer.Ordinal);
}
