// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Models;

public partial class Meta
{
    /// <summary>
    /// <see cref="LastUpdated"/> parsed as a <see cref="DateTimeOffset"/>. FHIR's <c>instant</c> type is
    /// always UTC on the wire, so this always reads/writes ISO 8601 in UTC. Named distinctly from the
    /// generated <see cref="LastUpdated"/> (a raw <c>string?</c>) rather than shadowing it, since the two
    /// can't share a name at different types on the same partial type.
    /// </summary>
    public DateTimeOffset? LastUpdatedOffset
    {
        get => LastUpdated is { } value && DateTimeOffset.TryParse(value, out var result) ? result : null;
        set => LastUpdated = value?.ToUniversalTime().ToString("o");
    }
}
