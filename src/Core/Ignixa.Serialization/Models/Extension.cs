// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;

namespace Ignixa.Models;

public partial class Extension
{
    /// <summary>
    /// Creates an <see cref="Extension"/> with <c>url</c> and <c>valueUri</c> set directly via the
    /// low-level JSON property mechanism, bypassing the typed R4/R5-specific <c>ValueUri</c> accessor.
    /// <c>value[x]</c> (including <c>valueUri</c>) is only generated on the R4/R5 subclasses -- it
    /// genuinely differs by version, so it's excluded from this shared base (see
    /// docs/features/typed-models/investigations/consolidate-handwritten-facades.md). This factory exists
    /// for callers that cannot reference the R4/R5 packages at all -- those packages are deliberately
    /// opt-in, not baked into the core request path (see docs/features/typed-models/readme.md's
    /// Constraints section) -- and so cannot construct <c>Ignixa.Models.R4.Extension</c>/<c>R5.Extension</c>
    /// directly.
    /// </summary>
    /// <remarks>
    /// Deliberately shaped as a factory rather than an instance mutator, and <c>internal</c> rather than
    /// <c>public</c>: the bypass does NOT participate in choice-variant clearing (no other <c>value[x]</c>
    /// key is removed), so calling it on an <see cref="Extension"/> that already has a different
    /// <c>value[x]</c> variant set would silently produce spec-invalid FHIR JSON with two <c>value[x]</c>
    /// keys. Always constructing a brand-new instance here removes that failure mode structurally --
    /// there is no existing state to conflict with -- rather than relying on a comment and caller
    /// discipline. <c>internal</c> plus <c>InternalsVisibleTo("Ignixa.Application")</c> (see
    /// <c>AssemblyInfo.cs</c>) keeps the bypass reachable only from its one legitimate caller
    /// (<c>SecurityCapabilitySegment.cs</c>) instead of every consumer of <c>Ignixa.Models</c>. If you can
    /// reference the R4/R5 packages and need full choice-clearing correctness, construct the
    /// version-specific subclass directly and use its typed <c>ValueUri</c> property instead.
    /// </remarks>
    internal static Extension CreateWithRawValueUri(string url, string? valueUri, FhirVersion? fhirVersion = null)
    {
        var extension = new Extension { FhirVersion = fhirVersion, Url = url };
        extension.SetProperty("valueUri", valueUri);
        return extension;
    }
}
