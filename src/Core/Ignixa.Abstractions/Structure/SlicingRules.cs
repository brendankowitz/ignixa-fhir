// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Abstractions;

/// <summary>
/// Rules that define whether unsliced content is allowed in a sliced FHIR element.
/// </summary>
public enum SlicingRules
{
    /// <summary>Additional content is allowed anywhere in the sliced element.</summary>
    Open,

    /// <summary>Additional content that does not match a slice is not allowed.</summary>
    Closed,

    /// <summary>Additional content is allowed only after all matched slices.</summary>
    OpenAtEnd,
}
