// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.Infrastructure;

/// <summary>
/// Classifies a tenant hostname configuration problem so callers can make fatality decisions on the
/// typed kind instead of matching on message text.
/// </summary>
public enum HostnameProblemKind
{
    /// <summary>The hostname is not a bare lowercase DNS host (scheme, port, path, or invalid label).</summary>
    Format,

    /// <summary>The hostname is claimed by more than one tenant.</summary>
    Duplicate,
}
