// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Domain.Models;
using Medino;

namespace Ignixa.Application.BackgroundOperations.BulkDelete;

/// <summary>
/// Validates a <c>$bulk-delete</c> request, snapshots its resource-type set, persists the job and starts
/// its orchestration. Every validation failure is a <see cref="Ignixa.Domain.Exceptions.BadRequestException"/>
/// or another 400-mapped <see cref="Ignixa.Serialization.Abstractions.FhirException"/>.
/// </summary>
/// <param name="TenantId">Owning tenant.</param>
/// <param name="ResourceType">Route resource type; null for the system-level route.</param>
/// <param name="SearchParameters">
/// Query parameters with the bulk-delete control flags (<c>_hardDelete</c>, <c>hardDelete</c>,
/// <c>_purgeHistory</c>, <c>excludedResourceTypes</c>, <c>_remove-references</c>) already removed.
/// <c>_type</c>, <c>_format</c> and <c>_pretty</c> may still be present; the handler interprets or drops them.
/// </param>
/// <param name="Mode">Effective deletion mode (hard takes precedence over purge).</param>
/// <param name="ExcludedResourceTypes">Types never deleted, including from the include cascade.</param>
/// <param name="RemoveReferences">Rewrite referrers of each deleted resource; requires <see cref="BulkDeleteMode.HardDelete"/>.</param>
/// <param name="RequestUrl">Original request URL, kept for diagnostics.</param>
public record CreateBulkDeleteJobCommand(
    int TenantId,
    string? ResourceType,
    IReadOnlyList<KeyValuePair<string, string>> SearchParameters,
    BulkDeleteMode Mode,
    IReadOnlyList<string> ExcludedResourceTypes,
    bool RemoveReferences,
    string? RequestUrl) : IRequest<CreateBulkDeleteJobResult>;
