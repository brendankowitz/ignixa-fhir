// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Serialization;
using Microsoft.Extensions.Logging;

namespace Ignixa.DataLayer.BlobStorage;

/// <summary>
/// Helper class to load ViewDefinition resources from the FHIR datastore.
/// ViewDefinitions are FHIR resources (resourceType: "ViewDefinition") that can be fetched like any other resource.
/// </summary>
public class ViewDefinitionLoader
{
    private readonly IFhirRepositoryFactory _repositoryFactory;
    private readonly ILogger<ViewDefinitionLoader> _logger;

    public ViewDefinitionLoader(
        IFhirRepositoryFactory repositoryFactory,
        ILogger<ViewDefinitionLoader> logger)
    {
        _repositoryFactory = repositoryFactory ?? throw new ArgumentNullException(nameof(repositoryFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Loads a ViewDefinition resource from the datastore and converts it to ISourceNavigator for evaluation.
    /// </summary>
    /// <param name="tenantId">Tenant ID to fetch from</param>
    /// <param name="viewDefinitionId">ViewDefinition resource ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>ViewDefinition as ISourceNavigator for SqlOnFhirEvaluator</returns>
    /// <exception cref="InvalidOperationException">Thrown if ViewDefinition not found</exception>
    public async Task<ISourceNavigator> LoadViewDefinitionAsync(
        int tenantId,
        string viewDefinitionId,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Loading ViewDefinition: {ViewDefinitionId} for tenant {TenantId}",
            viewDefinitionId,
            tenantId);

        // Get repository for tenant
        var repository = await _repositoryFactory.GetRepositoryAsync(tenantId, cancellationToken);

        // Fetch ViewDefinition resource using standard FHIR read
        var resourceKey = new ResourceKey("ViewDefinition", viewDefinitionId);
        var searchResult = await repository.GetAsync(resourceKey, cancellationToken);

        if (searchResult == null)
        {
            throw new InvalidOperationException(
                $"ViewDefinition not found: {viewDefinitionId} in tenant {tenantId}");
        }

        // Parse resource bytes directly to ResourceJsonNode
        var resourceNode = JsonSourceNodeFactory.Parse(searchResult.ResourceBytes);

        if (resourceNode == null)
        {
            throw new InvalidOperationException(
                $"Failed to parse ViewDefinition: {viewDefinitionId}");
        }

        _logger.LogDebug(
            "Successfully loaded ViewDefinition: {ViewDefinitionId}",
            viewDefinitionId);

        return resourceNode.ToSourceNavigator();
    }
}
