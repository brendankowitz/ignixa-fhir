// <copyright file="GetSystemHistoryQuery.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using Ignixa.Domain.Models;
using Medino;

namespace Ignixa.Application.Features.History;

/// <summary>
/// Query to retrieve version history across all resource types in the system.
/// System-level history: GET [base]/_history
/// </summary>
/// <param name="TenantId">Tenant partition ID.</param>
/// <param name="Parameters">Query parameters (count, offset, since, until, sort).</param>
/// <param name="BaseUrl">Base URL for pagination links.</param>
/// <param name="RequestPath">Request path for self link (e.g., "/_history").</param>
public sealed record GetSystemHistoryQuery(
    int TenantId,
    HistoryQueryParameters Parameters,
    string BaseUrl,
    string RequestPath) : IRequest<HistoryResult>;
