// <copyright file="GetSystemHistoryHandler.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using Microsoft.Extensions.Logging;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Medino;

namespace Ignixa.Application.Features.History;

/// <summary>
/// Handler for retrieving system-level history (all resources).
/// Returns streaming result for efficient memory usage.
/// </summary>
public sealed class GetSystemHistoryHandler : IRequestHandler<GetSystemHistoryQuery, HistoryResult>
{
    private readonly IFhirRepositoryFactory _repositoryFactory;
    private readonly ILogger<GetSystemHistoryHandler> _logger;

    public GetSystemHistoryHandler(
        IFhirRepositoryFactory repositoryFactory,
        ILogger<GetSystemHistoryHandler> logger)
    {
        _repositoryFactory = repositoryFactory ?? throw new ArgumentNullException(nameof(repositoryFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<HistoryResult> HandleAsync(GetSystemHistoryQuery request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        _logger.LogDebug(
            "Retrieving system-wide history (count={Count}, offset={Offset}, total={Total})",
            request.Parameters.Count,
            request.Parameters.Offset,
            request.Parameters.Total);

        // Get repository for tenant
        var repository = await _repositoryFactory.GetRepositoryAsync(request.TenantId, cancellationToken);

        // Execute system history query (returns truly streaming IAsyncEnumerable)
        var entries = repository.GetSystemHistoryAsync(
            request.TenantId,
            request.Parameters,
            cancellationToken);

        // Calculate total count only if explicitly requested (expensive separate query)
        int? totalCount = null;
        if (request.Parameters.Total == TotalMode.Accurate)
        {
            _logger.LogDebug("Calculating accurate total count for system-wide history (expensive query)");
            totalCount = await HistoryCountHelper.CountSystemHistoryAsync(repository, request.TenantId, request.Parameters, cancellationToken);
        }

        // Build pagination links (handles nullable totalCount)
        var links = HistoryPaginationLinkBuilder.BuildLinks(
            request.BaseUrl,
            request.RequestPath,
            request.Parameters,
            totalCount);

        _logger.LogInformation(
            "Streaming system-wide history (total={Total})",
            totalCount?.ToString() ?? "unknown");

        // Return streaming result
        return new HistoryResult
        {
            Entries = entries,
            TotalCount = totalCount,
            Links = links
        };
    }
}
