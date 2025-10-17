// <copyright file="GetResourceHistoryQuery.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using Ignixa.Domain.Models;
using Medino;

namespace Ignixa.Application.Features.History;

/// <summary>
/// Query to retrieve version history for a specific resource instance.
/// Instance-level history: GET [base]/[type]/[id]/_history
/// </summary>
/// <param name="ResourceType">FHIR resource type (e.g., "Patient").</param>
/// <param name="ResourceId">Resource ID.</param>
/// <param name="TenantId">Tenant partition ID.</param>
/// <param name="Parameters">Query parameters (count, offset, since, until, sort).</param>
/// <param name="BaseUrl">Base URL for pagination links (e.g., "https://api.example.com").</param>
/// <param name="RequestPath">Request path for self link (e.g., "/Patient/123/_history").</param>
public sealed record GetResourceHistoryQuery(
    string ResourceType,
    string ResourceId,
    int TenantId,
    HistoryQueryParameters Parameters,
    string BaseUrl,
    string RequestPath) : IRequest<HistoryResult>;
