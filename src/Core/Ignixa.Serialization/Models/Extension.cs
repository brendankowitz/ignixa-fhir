// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;

namespace Ignixa.Models;

public partial class Extension
{
    /// <summary>
    /// Sets a <c>value[x]</c> choice-type key (e.g. <c>"valueString"</c>, <c>"valueUri"</c>) directly via
    /// the low-level JSON property mechanism, clearing any other <c>value[x]</c> key first. <c>value[x]</c>
    /// is only generated with typed accessors on the R4/R5 subclasses -- it genuinely differs by version,
    /// so it's excluded from this shared base (see
    /// docs/features/typed-models/investigations/consolidate-handwritten-facades.md). This exists for
    /// callers that cannot reference the R4/R5 packages at all -- those packages are deliberately opt-in,
    /// not baked into the core request path (see docs/features/typed-models/readme.md's Constraints
    /// section) -- or that want a version-agnostic fake/test-fixture builder unconstrained by which FHIR
    /// version's choice-type union it targets.
    /// </summary>
    /// <remarks>
    /// Unlike a same-named hand-written property would be, this is safe to add to the shared base:
    /// FHIR's <c>value[x]</c> wire convention names every choice-type key <c>"value"</c> + PascalCase(type
    /// name) in every version, and <see cref="Extension"/> has no other property that begins with
    /// <c>"value"</c> (only <c>url</c>, <c>id</c>, <c>extension</c> do not) -- so "remove every existing
    /// property whose name starts with <c>value</c>, then set the new one" is exactly equivalent to the
    /// generated per-version <c>SetValueVariant</c>'s enumerated clear, without needing to know which
    /// variants exist for a given FHIR version. That means this method is safe to call more than once, or
    /// after a different <c>value[x]</c> variant is already present -- unlike a bare low-level property set,
    /// it can never leave two <c>value[x]</c> keys behind. If you can reference the R4/R5 packages and need
    /// the ergonomics of a typed property (not just clearing correctness), construct the version-specific
    /// subclass directly and use its typed accessor instead.
    /// </remarks>
    internal void SetValueChoiceRaw(string valueElementName, string? value)
    {
        foreach (string key in MutableNode.Select(property => property.Key)
            .Where(key => key.StartsWith("value", StringComparison.Ordinal) && key != valueElementName)
            .ToList())
        {
            MutableNode.Remove(key);
        }

        SetProperty(valueElementName, value);
    }

    /// <summary>
    /// Creates an <see cref="Extension"/> with <c>url</c> and <c>valueUri</c> set via
    /// <see cref="SetValueChoiceRaw"/>. This exists for callers that cannot reference the R4/R5 packages
    /// at all -- those packages are deliberately opt-in, not baked into the core request path (see
    /// docs/features/typed-models/readme.md's Constraints section) -- and so cannot construct
    /// <c>Ignixa.Models.R4.Extension</c>/<c>R5.Extension</c> directly.
    /// </summary>
    internal static Extension CreateWithRawValueUri(string url, string? valueUri, FhirVersion? fhirVersion = null)
    {
        var extension = new Extension { FhirVersion = fhirVersion, Url = url };
        extension.SetValueChoiceRaw("valueUri", valueUri);
        return extension;
    }
}
