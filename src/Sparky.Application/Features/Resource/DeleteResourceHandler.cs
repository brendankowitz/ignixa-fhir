// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Medino;
using Microsoft.Extensions.Logging;

namespace Sparky.Application.Features.Resource;

/// <summary>
/// Generic handler for deleting any FHIR resource.
/// Note: DELETE is not yet implemented in IFhirRepository (prototype phase).
/// This handler is a placeholder for Phase 1.1.
/// </summary>
public class DeleteResourceHandler : IRequestHandler<DeleteResourceCommand, bool>
{
    private readonly ILogger<DeleteResourceHandler> _logger;

    public DeleteResourceHandler(ILogger<DeleteResourceHandler> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<bool> HandleAsync(DeleteResourceCommand command, CancellationToken cancellationToken)
    {
        _logger.LogWarning("DELETE {ResourceType}/{Id} - Not yet implemented (Phase 1.1 placeholder)",
            command.ResourceType, command.Id);

        // TODO: Implement IFhirRepository.DeleteAsync in Phase 1.1
        // For now, return false (not found)
        return Task.FromResult(false);
    }
}
