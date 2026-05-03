// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Application.Infrastructure;
using Ignixa.DeId;
using Ignixa.DeId.Configuration;
using Ignixa.DeId.Darts.Configuration;
using Ignixa.DeId.Models;
using Ignixa.DeId.Pipeline;
using Medino;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.Operations.Features.DeIdentify;

/// <summary>
/// Handler for FHIR $de-identify operation.
/// De-identifies a FHIR resource using DARTS policy configuration from a Library resource.
/// </summary>
public class DeIdentifyHandler : IRequestHandler<DeIdentifyCommand, DeIdentifyResult>
{
    private readonly IDeIdPipeline _pipeline;
    private readonly LibraryConfigurationLoader _configLoader;
    private readonly IFhirRequestContextAccessor _contextAccessor;
    private readonly ILogger<DeIdentifyHandler> _logger;

    public DeIdentifyHandler(
        IDeIdPipeline pipeline,
        LibraryConfigurationLoader configLoader,
        IFhirRequestContextAccessor contextAccessor,
        ILogger<DeIdentifyHandler> logger)
    {
        _pipeline = pipeline;
        _configLoader = configLoader;
        _contextAccessor = contextAccessor;
        _logger = logger;
    }

    public async Task<DeIdentifyResult> HandleAsync(
        DeIdentifyCommand request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Executing $de-identify for tenant {TenantId} with policy {Policy}",
            request.TenantId,
            request.Policy);

        DeIdOptions options;
        try
        {
            options = _configLoader.LoadFromLibrary(request.ConfigurationLibrary);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load de-identification configuration from Library resource.");
            return new DeIdentifyResult(
                false,
                null,
                $"Configuration error: {ex.Message}");
        }

        var context = _contextAccessor.RequestContext
            ?? throw new InvalidOperationException("FHIR request context not available");

        var tenantConfig = context.TenantConfiguration;
        var fhirVersionEnum = context.FhirVersion;
        var schemaProvider = context.VersionContext!.GetSchemaProvider(fhirVersionEnum, tenantConfig!.TenantId);

        var settings = new RequestOptions
        {
            IsPrettyOutput = false,
            ValidateInput = options.Processing?.ValidateInput ?? false,
            ValidateOutput = options.Processing?.ValidateOutput ?? false,
        };

        var element = request.InputResource.ToElement(schemaProvider);

        var deIdContext = new DeIdContext(
            request.InputResource,
            element,
            schemaProvider,
            settings,
            options);

        var result = await _pipeline.ExecuteAsync(deIdContext, cancellationToken);

        if (!result.IsSuccess)
        {
            _logger.LogWarning("De-identification failed: {Error}", result.Error.Message);
            return new DeIdentifyResult(false, null, result.Error.Message);
        }

        return new DeIdentifyResult(true, result.Value.Resource, null);
    }
}
