// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Domain.Abstractions;

namespace Ignixa.Domain.Models;

/// <summary>
/// Immutable bulk-delete job definition (input parameters) for use with BackgroundJob&lt;BulkDeleteJobDefinition&gt;.
/// Represents the configuration of a FHIR <c>$bulk-delete</c> operation.
/// TenantId is stored here (in the payload), not as a BackgroundJob property.
/// </summary>
public class BulkDeleteJobDefinition : IJobDefinition
{
    /// <summary>
    /// Tenant ID for multi-tenancy isolation (stored in definition payload, not schema).
    /// </summary>
    public required int TenantId { get; init; }

    /// <summary>
    /// The snapshot of the resource type set to process, in order. Deterministic at kickoff time so
    /// replay (after a crash or a retry) targets exactly the same types regardless of later writes.
    /// </summary>
    public required IReadOnlyList<string> ResourceTypes { get; init; }

    /// <summary>
    /// The raw query string of search filters, with the bulk-delete control parameters
    /// (<c>_hardDelete</c>, <c>hardDelete</c>, <c>_purgeHistory</c>, <c>excludedResourceTypes</c>,
    /// <c>_remove-references</c>, <c>_type</c>) removed. May be empty.
    /// </summary>
    public required string SearchQuery { get; init; }

    /// <summary>
    /// The physical effect to apply to each matched resource.
    /// </summary>
    public required BulkDeleteMode Mode { get; init; }

    /// <summary>
    /// Resource types excluded from deletion, including from the <c>_include</c>/<c>_revinclude</c> cascade.
    /// </summary>
    public required IReadOnlyList<string> ExcludedResourceTypes { get; init; }

    /// <summary>
    /// When true, referrers of a hard-deleted resource have their reference rewritten (the <c>reference</c>
    /// element is removed and <c>display</c> set to "Referenced resource deleted") and a new version saved.
    /// Valid only with <see cref="BulkDeleteMode.HardDelete"/>.
    /// </summary>
    public bool RemoveReferences { get; init; }

    /// <summary>
    /// Whether this job was kicked off at the system level (no resource-type route segment).
    /// </summary>
    public bool IsSystemLevel { get; init; }

    /// <summary>
    /// Original absolute HTTP request URL, including search and control parameters, for diagnostics.
    /// Absent for older jobs and jobs initiated outside HTTP.
    /// </summary>
    public string? RequestUrl { get; init; }
}
