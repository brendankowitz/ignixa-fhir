// <copyright file="GetResourceHistoryHandler.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using Microsoft.Extensions.Logging;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Medino;

namespace Ignixa.Application.Features.History;

/// <summary>
/// Handler for retrieving resource instance-level history.
/// Returns streaming result for efficient memory usage.
/// </summary>
public sealed class GetResourceHistoryHandler : IRequestHandler<GetResourceHistoryQuery, HistoryResult>
{
    private readonly IFhirRepositoryFactory _repositoryFactory;
    private readonly ILogger<GetResourceHistoryHandler> _logger;

    public GetResourceHistoryHandler(
        IFhirRepositoryFactory repositoryFactory,
        ILogger<GetResourceHistoryHandler> _logger)
    {
        _repositoryFactory = repositoryFactory ?? throw new ArgumentNullException(nameof(repositoryFactory));
        this._logger = _logger ?? throw new ArgumentNullException(nameof(_logger));
    }

    public async Task<HistoryResult> HandleAsync(GetResourceHistoryQuery request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        _logger.LogDebug(
            "Retrieving history for {ResourceType}/{ResourceId} (count={Count}, offset={Offset}, total={Total})",
            request.ResourceType,
            request.ResourceId,
            request.Parameters.Count,
            request.Parameters.Offset,
            request.Parameters.Total);

        // Get repository for tenant
        var repository = await _repositoryFactory.GetRepositoryAsync(request.TenantId, cancellationToken);

        // Execute history query (returns truly streaming IAsyncEnumerable)
        var key = new ResourceKey(request.ResourceType, request.ResourceId, null, request.TenantId);
        var entries = repository.GetResourceHistoryAsync(key, request.Parameters, cancellationToken);

        // Calculate total count only if explicitly requested (expensive separate query)
        int? totalCount = null;
        if (request.Parameters.Total == TotalMode.Accurate)
        {
            _logger.LogDebug("Calculating accurate total count for {ResourceType}/{ResourceId} (expensive query)", request.ResourceType, request.ResourceId);
            totalCount = await HistoryCountHelper.CountResourceHistoryAsync(repository, key, request.Parameters, cancellationToken);
        }

        // Build pagination links (handles nullable totalCount)
        var links = HistoryPaginationLinkBuilder.BuildLinks(
            request.BaseUrl,
            request.RequestPath,
            request.Parameters,
            totalCount);

        _logger.LogInformation(
            "Streaming history for {ResourceType}/{ResourceId} (total={Total})",
            request.ResourceType,
            request.ResourceId,
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
