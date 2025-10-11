// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.ElementModel;
using Medino;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Sparky.Application.Features.Bundle;
using Sparky.Application.Infrastructure;
using Sparky.Domain.Abstractions;
using Sparky.Domain.Models;
using Sparky.Extensions;
using Sparky.Search.Indexing;

namespace Sparky.Application.Features.Resource;

/// <summary>
/// Generic handler for creating or updating any FHIR resource.
/// Replaces resource-specific handlers like CreateOrUpdatePatientHandler.
/// Supports both immediate writes (standalone operations) and deferred writes (bundle operations).
/// Coordinator can be passed via command parameter OR via HttpContext.Items (pipeline routing).
/// </summary>
public class CreateOrUpdateResourceHandler : IRequestHandler<CreateOrUpdateResourceCommand, ResourceKey>
{
    private readonly IFhirRepository _repository;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IFhirVersionContext _fhirVersionContext;
    private readonly ILogger<CreateOrUpdateResourceHandler> _logger;

    public CreateOrUpdateResourceHandler(
        IFhirRepository repository,
        IHttpContextAccessor httpContextAccessor,
        IFhirVersionContext fhirVersionContext,
        ILogger<CreateOrUpdateResourceHandler> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
        _fhirVersionContext = fhirVersionContext ?? throw new ArgumentNullException(nameof(fhirVersionContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ResourceKey> HandleAsync(CreateOrUpdateResourceCommand command, CancellationToken cancellationToken)
    {
        // Business logic - always runs for both bundle and standalone operations
        _logger.LogInformation(
            "Processing CreateOrUpdateResource for {ResourceType}/{Id}",
            command.ResourceType,
            command.Id);

        // Create wrapper (needed for both paths now)
        var wrapper = await CreateResourceWrapperAsync(command, cancellationToken);

        ResourceKey key;

        // Resolve coordinator from command OR HttpContext.Items (pipeline routing fallback)
        DeferredWriteCoordinator? coordinator = command.Coordinator
            ?? _httpContextAccessor.HttpContext.GetDeferredWriteCoordinator();

        if (coordinator != null && command.Coordinator == null)
        {
            _logger.LogDebug(
                "Resolved DeferredWriteCoordinator from HttpContext.Items for {ResourceType}/{Id}",
                command.ResourceType,
                command.Id);
        }

        // Routing logic - coordinator presence determines path
        if (coordinator != null)
        {
            // Bundle path - queue for deferred batch write
            _logger.LogDebug(
                "Using deferred write coordinator for {ResourceType}/{Id}",
                command.ResourceType,
                command.Id);

            // Get entry index from HttpContext.Items if available
            int entryIndex = _httpContextAccessor.HttpContext.GetBundleEntryIndex();

            // Queue wrapper for deferred batch write
            key = await coordinator.QueueWriteAsync(
                wrapper,
                entryIndex,
                cancellationToken);
        }
        else
        {
            // Standalone path - write immediately to repository
            key = await _repository.CreateOrUpdateAsync(wrapper, cancellationToken);
        }

        // Success logging - always runs for both bundle and standalone operations
        _logger.LogInformation(
            "Created/Updated {ResourceType}/{Id} with version {VersionId}",
            key.ResourceType,
            key.Id,
            key.VersionId);

        return key;
    }

    /// <summary>
    /// Creates a ResourceWrapper from the command.
    /// Single place for wrapper construction logic.
    /// Extracts FHIR version from headers and search indices from resource.
    /// </summary>
    private async Task<ResourceWrapper> CreateResourceWrapperAsync(
        CreateOrUpdateResourceCommand command,
        CancellationToken cancellationToken)
    {
        var request = new ResourceRequest("PUT", $"{command.ResourceType}/{command.Id}");

        // Extract FHIR version from headers (defaults to R4)
        var fhirVersionEnum = FhirVersionExtractor.ExtractFhirVersion(_httpContextAccessor.HttpContext);

        // Get version-specific schema provider and search indexer from context
        var schemaProvider = _fhirVersionContext.GetSchemaProvider(fhirVersionEnum);
        var searchIndexer = await _fhirVersionContext.GetSearchIndexerAsync(fhirVersionEnum, cancellationToken);

        // Extract search indices using version-specific indexer
        IReadOnlyCollection<SearchIndexEntry>? searchIndices = null;
        try
        {
            // Convert ISourceNode to ITypedElement with version-specific type information
            // IFhirSchemaProvider extends IStructureDefinitionSummaryProvider, so we can use it directly
            var typedElement = command.Resource.ToTypedElement(schemaProvider);
            searchIndices = searchIndexer.Extract(typedElement);

            _logger.LogDebug(
                "Extracted {Count} search indices for {ResourceType}/{Id} (FHIR {Version})",
                searchIndices.Count,
                command.ResourceType,
                command.Id,
                fhirVersionEnum);
        }
        catch (Exception ex)
        {
            // Log but don't fail - search indexing is optional for now
            _logger.LogWarning(
                ex,
                "Failed to extract search indices for {ResourceType}/{Id} (FHIR {Version})",
                command.ResourceType,
                command.Id,
                fhirVersionEnum);
        }

        return new ResourceWrapper(
            command.ResourceType,
            command.Id,
            "1", // Version will be determined by repository
            DateTimeOffset.UtcNow,
            command.Resource,
            request,
            false) // isDeleted
        {
            RawJson = command.RawJson,
            FhirVersion = fhirVersionEnum.ToVersionString(), // Convert enum to string for storage
            SearchIndices = searchIndices
        };
    }
}
