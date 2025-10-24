// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Domain.Models;

/// <summary>
/// Indicates how a resource entry is related to a search.
/// Maps to FHIR Bundle.entry.search.mode
/// </summary>
public enum SearchEntryMode
{
    /// <summary>
    /// This resource matched the search specification.
    /// </summary>
    Match,

    /// <summary>
    /// This resource is included via _include or _revinclude.
    /// </summary>
    Include,

    /// <summary>
    /// This entry contains an OperationOutcome.
    /// </summary>
    Outcome
}
