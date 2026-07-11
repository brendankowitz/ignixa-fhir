// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Models;

public partial class Extension
{
    /// <summary>
    /// Sets <c>valueUri</c> directly via the low-level JSON property mechanism, bypassing the typed
    /// R4/R5-specific <c>ValueUri</c> accessor. <c>value[x]</c> (including <c>valueUri</c>) is only
    /// generated on the R4/R5 subclasses -- it genuinely differs by version, so it's excluded from this
    /// shared base (see docs/features/typed-models/investigations/consolidate-handwritten-facades.md).
    /// This method exists for callers that cannot reference the R4/R5 packages at all -- those packages
    /// are deliberately opt-in, not baked into the core request path (see
    /// docs/features/typed-models/readme.md's Constraints section) -- and so cannot construct
    /// <c>Ignixa.Models.R4.Extension</c>/<c>R5.Extension</c> directly. Unlike the typed accessor, this
    /// does NOT participate in choice-variant clearing (no other <c>value[x]</c> key is removed) -- safe
    /// only for an extension that sets exactly one variant, once, and never sets a different variant
    /// afterward. If you can reference the R4/R5 packages and need full choice-clearing correctness,
    /// construct the version-specific subclass directly and use its typed <c>ValueUri</c> property
    /// instead of this method.
    /// </summary>
    public void SetValueUriRaw(string? value) => SetProperty("valueUri", value);
}
