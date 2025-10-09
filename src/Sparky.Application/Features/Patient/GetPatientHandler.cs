// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Medino;
using Microsoft.Extensions.Logging;
using Sparky.Domain.Abstractions;
using Sparky.Domain.Models;

namespace Sparky.Application.Features.Patient;

public class GetPatientHandler : IRequestHandler<GetPatientQuery, ResourceWrapper?>
{
    private readonly IFhirRepository _repository;
    private readonly ILogger<GetPatientHandler> _logger;

    public GetPatientHandler(
        IFhirRepository repository,
        ILogger<GetPatientHandler> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ResourceWrapper?> HandleAsync(GetPatientQuery query, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Processing GetPatient for ID: {PatientId}", query.PatientId);

        var key = new ResourceKey("Patient", query.PatientId);
        ResourceWrapper? result = await _repository.GetAsync(key, cancellationToken);

        if (result == null)
        {
            _logger.LogInformation("Patient not found: {PatientId}", query.PatientId);
        }
        else
        {
            _logger.LogDebug("Retrieved Patient {PatientId} version {VersionId}",
                result.ResourceId, result.VersionId);
        }

        return result;
    }
}
