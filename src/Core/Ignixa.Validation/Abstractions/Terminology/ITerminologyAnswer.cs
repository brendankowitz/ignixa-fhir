// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Validation.Abstractions.Terminology;

/// <summary>An answer that may be undetermined. <see cref="Unknown"/> is null exactly when the answer is determined.</summary>
public interface ITerminologyAnswer
{
    UnknownReason? Unknown { get; }
}
