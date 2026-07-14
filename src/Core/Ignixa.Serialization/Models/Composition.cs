// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;

namespace Ignixa.Models;

public partial class Composition
{
    /// <summary>
    /// Sets <c>status</c> directly via the low-level JSON property mechanism. <c>status</c> is a
    /// version-tagged enum on the R4/R5 subclasses (not the shared base) because the two FHIR versions'
    /// <c>composition-status</c> value sets differ (R5 adds several literals R4 doesn't have) -- this
    /// exists for callers (like <c>IpsGeneratorService</c>) that need to write a status literal common to
    /// both value sets without referencing the R4/R5 packages at all, matching the same low-level
    /// escape-hatch pattern as <see cref="Extension.SetValueChoiceRaw"/>.
    /// </summary>
    internal void SetStatusRaw(string statusCode) => SetProperty("status", JsonValue.Create(statusCode));

    /// <summary>
    /// Sets <c>subject</c> directly via the low-level JSON property mechanism, always in R4's shape (a
    /// single Reference). R5 changed <c>subject</c> to a list (0..*); this setter is only correct for
    /// callers that are deliberately producing R4-shaped Composition JSON (see
    /// docs/features/typed-models/investigations/consolidate-handwritten-facades.md's IPS notes -- the
    /// IPS IG this exists for is permanently R4-based).
    /// </summary>
    internal void SetSubjectRaw(Reference subject) => SetProperty("subject", subject?.MutableNode);
}
