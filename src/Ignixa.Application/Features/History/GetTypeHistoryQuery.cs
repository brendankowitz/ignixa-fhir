// <copyright file="GetTypeHistoryQuery.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using Ignixa.Domain.Models;
using Medino;

namespace Ignixa.Application.Features.History;

/// <summary>
/// Query to retrieve version history for all resources of a given type.
/// Type-level history: GET [base]/[type]/_history
/// </summary>
/// <param name="ResourceType">FHIR resource type (e.g., "Patient").</param>
/// <param name="TenantId">Tenant partition ID.</param>
/// <param name="Parameters">Query parameters (count, offset, since, until, sort).</param>
/// <param name="BaseUrl">Base URL for pagination links.</param>
/// <param name="RequestPath">Request path for self link (e.g., "/Patient/_history").</param>
public sealed record GetTypeHistoryQuery(
    string ResourceType,
    int TenantId,
    HistoryQueryParameters Parameters,
    string BaseUrl,
    string RequestPath) : IRequest<HistoryResult>;
