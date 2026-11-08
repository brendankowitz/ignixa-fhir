// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.Abstractions;

namespace Ignixa.Validation.Checks;

/// <summary>
/// Answers "does this JSON-backed element certainly not carry a member?" from the raw object, without the child lookup
/// <see cref="IElement.Children(string?)"/> performs. Checks that test one name per element of a datatype on every
/// validated node use it to skip names the document does not carry, which most are.
/// </summary>
internal static class RawMembers
{
    /// <summary>
    /// True only when <paramref name="element"/> is backed by a JSON object holding neither <paramref name="name"/> nor its
    /// shadow <c>_name</c>, so <c>Children(name)</c> is certainly empty. False when the element is not JSON-backed or either
    /// member is present, and the caller then asks <see cref="IElement.Children(string?)"/> as usual. Not valid for a choice
    /// element's base name (<c>value</c>), whose children are its concrete variants.
    /// </summary>
    /// <param name="element">The element whose members are tested.</param>
    /// <param name="name">The exact member name.</param>
    /// <param name="shadowName">The member's shadow name, <c>_</c> followed by <paramref name="name"/>.</param>
    /// <returns>Whether the member is certainly absent.</returns>
    public static bool CertainlyAbsent(IElement element, string name, string shadowName) =>
        element.Meta<JsonNode>() is JsonObject raw && !raw.ContainsKey(name) && !raw.ContainsKey(shadowName);
}
