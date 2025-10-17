// <copyright file="GetTypeHistoryHandler.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using Microsoft.Extensions.Logging;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Medino;

namespace Ignixa.Application.Features.History;

/// <summary>
/// Handler for retrieving resource type-level history.
/// Returns streaming result for efficient memory usage.
/// </summary>
public sealed class GetTypeHistoryHandler : IRequestHandler<GetTypeHistoryQuery, HistoryResult>
{
    private readonly IFhirRepositoryFactory _repositoryFactory;
    private readonly ILogger<GetTypeHistoryHandler> _logger;

    public GetTypeHistoryHandler(
        IFhirRepositoryFactory repositoryFactory,
        ILogger<GetTypeHistoryHandler> logger)
    {
        _repositoryFactory = repositoryFactory ?? throw new ArgumentNullException(nameof(repositoryFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<HistoryResult> HandleAsync(GetTypeHistoryQuery request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        _logger.LogDebug(
            "Retrieving history for resource type {ResourceType} (count={Count}, offset={Offset}, total={Total})",
            request.ResourceType,
            request.Parameters.Count,
            request.Parameters.Offset,
            request.Parameters.Total);

        // Get repository for tenant
        var repository = await _repositoryFactory.GetRepositoryAsync(request.TenantId, cancellationToken);

        // Execute type history query (returns truly streaming IAsyncEnumerable)
        var entries = repository.GetTypeHistoryAsync(
            request.ResourceType,
            request.TenantId,
            request.Parameters,
            cancellationToken);

        // Calculate total count only if explicitly requested (expensive separate query)
        int? totalCount = null;
        if (request.Parameters.Total == TotalMode.Accurate)
        {
            _logger.LogDebug("Calculating accurate total count for resource type {ResourceType} (expensive query)", request.ResourceType);
            totalCount = await HistoryCountHelper.CountTypeHistoryAsync(repository, request.ResourceType, request.TenantId, request.Parameters, cancellationToken);
        }

        // Build pagination links (handles nullable totalCount)
        var links = HistoryPaginationLinkBuilder.BuildLinks(
            request.BaseUrl,
            request.RequestPath,
            request.Parameters,
            totalCount);

        _logger.LogInformation(
            "Streaming history for resource type {ResourceType} (total={Total})",
            request.ResourceType,
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
