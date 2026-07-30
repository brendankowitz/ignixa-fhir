// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using DurableTask.Core;
using Ignixa.Abstractions;
using Ignixa.Application.BackgroundOperations.Terminology.Models;
using Ignixa.Application.Infrastructure;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Terminology;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.BackgroundOperations.Terminology.Activities;

/// <summary>
/// DurableTask activity for importing a single terminology resource (CodeSystem, ValueSet, or ConceptMap).
/// Loads the PackageResource, routes it to the matching <see cref="ITerminologyImporter"/> method, and
/// reports the outcome back to the orchestration.
/// <para>
/// <b>The importer owns <c>TerminologyImportStatus</c>; this activity must not write it on any path the
/// importer reaches.</b> Stamping <c>InProgress</c> here before handing off is what previously made the
/// importer's unchanged-content guard unreachable — that guard tests the status on the row, so it never saw
/// anything but <c>InProgress</c> and every package load re-imported every terminology resource in full.
/// Writing <c>result.Status</c> back afterwards then overwrote <c>Completed</c> rows with <c>Skipped</c>,
/// which <c>HybridTerminologyService</c> reads as "not in the database" and answers from the in-memory
/// fallback instead.
/// </para>
/// <para>
/// The exception is a resource the importer was never called for, which nothing else can record. That goes
/// through <see cref="IPackageResourceRepository.MarkTerminologyImportFailedAsync"/>.
/// </para>
/// </summary>
public class ImportTerminologyResourceActivity : AsyncTaskActivity<ImportTerminologyResourceInput, ImportTerminologyResourceOutput>
{
    private static readonly string[] SupportedResourceTypes = ["CodeSystem", "ValueSet", "ConceptMap"];

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ImportTerminologyResourceActivity> _logger;

    public ImportTerminologyResourceActivity(
        IServiceProvider serviceProvider,
        ILogger<ImportTerminologyResourceActivity> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task<ImportTerminologyResourceOutput> ExecuteAsync(
        TaskContext context,
        ImportTerminologyResourceInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        using var scope = _serviceProvider.CreateScope();
        var packageResources = scope.ServiceProvider.GetRequiredService<IPackageResourceRepository>();
        var importerFactory = scope.ServiceProvider.GetRequiredService<ITerminologyImporterFactory>();
        var fhirContextAccessor = scope.ServiceProvider.GetRequiredService<IFhirRequestContextAccessor>();

        // Establish the ambient request context for the duration of the activity so any reference
        // resolution the importer performs resolves the same tenant-scoped base URIs the HTTP request
        // path would have. See ImportBatchActivity for the failure mode this prevents.
        var previousContext = fhirContextAccessor.RequestContext;
        fhirContextAccessor.RequestContext = FhirRequestContextFactory.CreateBackgroundContext(input.TenantId);

        try
        {
            return await ImportAsync(packageResources, importerFactory, input);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Unexpected error in ImportTerminologyResourceActivity for PackageResourceId {PackageResourceId}",
                input.PackageResourceId);

            return Failure(input.PackageResourceId, "unknown", "unknown", ex.Message);
        }
        finally
        {
            fhirContextAccessor.RequestContext = previousContext;
        }
    }

    private async Task<ImportTerminologyResourceOutput> ImportAsync(
        IPackageResourceRepository packageResources,
        ITerminologyImporterFactory importerFactory,
        ImportTerminologyResourceInput input)
    {
        _logger.LogInformation(
            "Starting terminology import for PackageResourceId {PackageResourceId}",
            input.PackageResourceId);

        var packageResource = await packageResources.GetByPackageResourceIdAsync(
            input.PackageResourceId, CancellationToken.None);

        if (packageResource is null)
        {
            // No row to record a status against, so this is reported to the orchestration only.
            _logger.LogError("PackageResource {PackageResourceId} not found", input.PackageResourceId);

            return Failure(
                input.PackageResourceId, "unknown", "unknown", $"PackageResource {input.PackageResourceId} not found");
        }

        _logger.LogDebug(
            "Loaded PackageResource: {Canonical} ({ResourceType}) from package {PackageId}@{PackageVersion}",
            packageResource.Canonical,
            packageResource.ResourceType,
            packageResource.PackageId,
            packageResource.PackageVersion);

        if (!SupportedResourceTypes.Contains(packageResource.ResourceType))
        {
            // Reachable only from a hand-built orchestration -- ListPendingTerminologyImportsAsync offers
            // nothing but the three supported types. Recorded because the importer is never called and so
            // cannot record it, leaving no trace of why the resource never imported. Failed rather than
            // Skipped deliberately: Skipped is terminal and would suppress the retry that adding support for
            // the type should get, whereas Failed carries a diagnosable message and stays retryable.
            var message = $"Unsupported ResourceType for terminology import: {packageResource.ResourceType}";
            _logger.LogError("{Message} (PackageResourceId: {PackageResourceId})", message, input.PackageResourceId);

            await packageResources.MarkTerminologyImportFailedAsync(
                input.PackageResourceId, message, CancellationToken.None);

            return Failure(input.PackageResourceId, packageResource.Canonical, packageResource.ResourceType, message);
        }

        var importer = await importerFactory.CreateAsync(CancellationToken.None);

        // The importer converts its own failures into a Failed status on the package row and a failure
        // result, so there is no catch here: an exception escaping it is a genuine fault and belongs to the
        // caller's handler.
        var result = packageResource.ResourceType switch
        {
            "CodeSystem" => await importer.ImportCodeSystemAsync(input.TenantId, packageResource, CancellationToken.None),
            "ValueSet" => await importer.ImportValueSetAsync(input.TenantId, packageResource, CancellationToken.None),
            "ConceptMap" => await importer.ImportConceptMapAsync(input.TenantId, packageResource, CancellationToken.None),

            // Unreachable past the SupportedResourceTypes guard above, and spelled out anyway so that adding
            // a fourth entry to that list does not quietly import it as a ConceptMap.
            _ => throw new InvalidOperationException(
                $"Unsupported ResourceType for terminology import: {packageResource.ResourceType}"),
        };

        _logger.LogInformation(
            "Completed terminology import for {Canonical} ({ResourceType}): {Status}, {ItemCount} concepts",
            packageResource.Canonical,
            packageResource.ResourceType,
            result.Status,
            result.ItemCount);

        return new ImportTerminologyResourceOutput(
            PackageResourceId: input.PackageResourceId,
            Canonical: packageResource.Canonical,
            ResourceType: packageResource.ResourceType,
            Success: result.Success,
            ConceptCount: result.ItemCount,
            ErrorMessage: result.ErrorMessage);
    }

    /// <summary>
    /// Failures are returned rather than thrown so one bad resource does not abort the orchestration's
    /// remaining imports.
    /// </summary>
    private static ImportTerminologyResourceOutput Failure(
        long packageResourceId, string canonical, string resourceType, string errorMessage)
        => new(
            PackageResourceId: packageResourceId,
            Canonical: canonical,
            ResourceType: resourceType,
            Success: false,
            ConceptCount: 0,
            ErrorMessage: errorMessage);
}
