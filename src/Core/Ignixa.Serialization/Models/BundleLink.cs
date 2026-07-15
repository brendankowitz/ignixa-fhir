// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;

namespace Ignixa.Models;

public partial class BundleLink
{
    /// <summary>
    /// Gets <c>relation</c> directly via the low-level JSON property mechanism. <c>relation</c> is a
    /// version-tagged string on the R4/R5 subclasses (not the shared base) because R5 tightened the
    /// binding strength of <c>Bundle.link.relation</c> against <c>iana-link-relations</c> -- but the wire
    /// shape is a plain string in both versions, so this exists for callers that need to read/write a
    /// relation literal common to both without referencing the R4/R5 packages at all, matching the same
    /// low-level escape-hatch pattern as <see cref="Extension.SetValueChoiceRaw"/>.
    /// </summary>
    internal string? GetRelationRaw() => MutableNode["relation"]?.GetValue<string>();

    /// <summary>
    /// Sets <c>relation</c> directly via the low-level JSON property mechanism. See <see cref="GetRelationRaw"/>.
    /// </summary>
    internal void SetRelationRaw(string relation) => SetProperty("relation", JsonValue.Create(relation));
}
