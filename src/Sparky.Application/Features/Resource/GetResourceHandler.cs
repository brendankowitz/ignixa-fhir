// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Medino;
using Microsoft.Extensions.Logging;
using Sparky.Domain.Abstractions;
using Sparky.Domain.Models;

namespace Sparky.Application.Features.Resource;

/// <summary>
/// Generic handler for retrieving any FHIR resource by type and ID.
/// Replaces resource-specific handlers like GetPatientHandler.
/// </summary>
public class GetResourceHandler : IRequestHandler<GetResourceQuery, ResourceWrapper?>
{
    private readonly IFhirRepository _repository;
    private readonly ILogger<GetResourceHandler> _logger;

    public GetResourceHandler(
        IFhirRepository repository,
        ILogger<GetResourceHandler> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ResourceWrapper?> HandleAsync(GetResourceQuery query, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Processing GetResource for {ResourceType}/{Id}", query.ResourceType, query.Id);

        var key = new ResourceKey(query.ResourceType, query.Id);
        ResourceWrapper? result = await _repository.GetAsync(key, cancellationToken);

        if (result == null)
        {
            _logger.LogInformation("{ResourceType} not found: {Id}", query.ResourceType, query.Id);
        }
        else
        {
            _logger.LogDebug("Retrieved {ResourceType}/{Id} version {VersionId}",
                result.ResourceType, result.ResourceId, result.VersionId);
        }

        return result;
    }
}
