// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;

namespace Ignixa.Models;

public partial class Bundle
{
    /// <summary>
    /// Gets <c>type</c> directly via the low-level JSON property mechanism. <c>type</c> is a
    /// version-tagged enum on the R4/R5 subclasses (not the shared base) because R5 adds a 10th literal
    /// ("subscription-notification") to the 9-literal R4 <c>bundle-type</c> value set -- but every real
    /// caller in this codebase only ever reads/writes one of the 9 literals common to both versions, so
    /// this raw accessor covers every real usage without needing a version-specific type. Public (not
    /// internal, unlike <see cref="Extension.SetValueChoiceRaw"/>'s narrow-friend-list escape hatch): this
    /// is read/written broadly across the codebase and its tests, not confined to one caller.
    /// </summary>
    public string? GetTypeRaw() => MutableNode["type"]?.GetValue<string>();

    /// <summary>
    /// Sets <c>type</c> directly via the low-level JSON property mechanism. See <see cref="GetTypeRaw"/>.
    /// </summary>
    public void SetTypeRaw(string type) => SetProperty("type", JsonValue.Create(type));
}
