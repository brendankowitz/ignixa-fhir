// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

namespace Ignixa.Abstractions;

/// <summary>
/// Provides read access to <c>CodeSystem</c> concept content (code &#8594; display, and membership)
/// sourced from loaded FHIR packages. This is a resolution surface only: it lets a terminology
/// service answer "what is the display for this code" and "does this system enumerate this code"
/// without pulling in the whole terminology stack. It intentionally does not perform binding
/// validation — that stays in <c>ITerminologyService</c>.
/// </summary>
public interface ICodeSystemProvider
{
    /// <summary>
    /// Gets the display text for a concept, or null when the system is unknown to this provider,
    /// the code is absent, or the concept carries no display.
    /// </summary>
    /// <param name="system">The code system canonical URL.</param>
    /// <param name="code">The concept code.</param>
    /// <returns>The concept display, or null.</returns>
    string? GetDisplay(string system, string code);

    /// <summary>
    /// Reports whether a code is a member of a code system. The tri-state return also encodes
    /// completeness: a non-null answer means the system is locally enumerable.
    /// </summary>
    /// <param name="system">The code system canonical URL.</param>
    /// <param name="code">The concept code.</param>
    /// <returns>
    /// True when the code is enumerated by the system; false when the system is enumerable but the
    /// code is absent; null when the system is unknown or not locally enumerable.
    /// </returns>
    bool? ContainsCode(string system, string code);
}
