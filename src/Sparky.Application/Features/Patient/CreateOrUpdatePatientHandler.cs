// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Medino;
using Microsoft.Extensions.Logging;
using Sparky.Domain.Abstractions;
using Sparky.Domain.Models;

namespace Sparky.Application.Features.Patient;

public class CreateOrUpdatePatientHandler : IRequestHandler<CreateOrUpdatePatientCommand, ResourceKey>
{
    private readonly IFhirRepository _repository;
    private readonly ILogger<CreateOrUpdatePatientHandler> _logger;

    public CreateOrUpdatePatientHandler(
        IFhirRepository repository,
        ILogger<CreateOrUpdatePatientHandler> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ResourceKey> HandleAsync(CreateOrUpdatePatientCommand command, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Processing CreateOrUpdatePatient for ID: {PatientId}", command.PatientId);

        var request = new ResourceRequest("PUT", $"Patient/{command.PatientId}");

        var wrapper = new ResourceWrapper(
            "Patient",
            command.PatientId,
            "1", // Will be incremented by repository
            DateTimeOffset.UtcNow,
            command.Resource,
            request,
            false);

        ResourceKey key = await _repository.CreateOrUpdateAsync(wrapper, cancellationToken);

        _logger.LogInformation("Created/Updated Patient {PatientId} with version {VersionId}",
            key.Id, key.VersionId);

        return key;
    }
}
