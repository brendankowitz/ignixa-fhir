// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Validation;

/// <summary>How many validated resources got each verdict.</summary>
/// <param name="Valid">Every check ran and none failed.</param>
/// <param name="Invalid">At least one check failed (an error or fatal issue).</param>
/// <param name="Unknown">No check failed, but at least one could not be evaluated (a binding not checked, a profile not loaded, ...): not proof of validity.</param>
/// <param name="Unreadable">The resource could not be read or parsed, so it was not validated.</param>
public sealed record VerdictCounts(int Valid, int Invalid, int Unknown, int Unreadable);
