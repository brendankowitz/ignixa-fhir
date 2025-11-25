// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.Terminology.Models;
using Ignixa.DataLayer.SqlEntityFramework;
using Ignixa.DataLayer.SqlEntityFramework.Features.Terminology;
using Ignixa.Domain.Models;
using Ignixa.Domain.Terminology;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.BackgroundOperations.Terminology.Activities;

/// <summary>
/// DurableTask activity for importing a single terminology resource (CodeSystem, ValueSet, or ConceptMap).
/// Loads PackageResource from database, routes to appropriate ITerminologyImporter method, and updates status.
/// Uses IServiceProvider to create scoped services (FhirDbContext, ITerminologyImporter) per activity execution.
/// </summary>
public class ImportTerminologyResourceActivity : AsyncTaskActivity<ImportTerminologyResourceInput, ImportTerminologyResourceOutput>
{
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
        // Create scope for this activity execution to get scoped services (FhirDbContext, ITerminologyImporter)
        using var scope = _serviceProvider.CreateScope();
        var repositoryFactory = scope.ServiceProvider.GetRequiredService<SqlEntityFrameworkRepositoryFactory>();
        var loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();

        await using var fhirDbContext = await repositoryFactory.GetDbContextAsync(input.TenantId, CancellationToken.None);
        var systemRepository = new SqlSystemRepository(fhirDbContext, loggerFactory.CreateLogger<SqlSystemRepository>());
        ITerminologyImporter terminologyImporter = new SqlCodeSystemImporter(
            fhirDbContext,
            systemRepository,
            loggerFactory.CreateLogger<SqlCodeSystemImporter>());

        try
        {
            _logger.LogInformation(
                "Starting terminology import for PackageResourceId {PackageResourceId}",
                input.PackageResourceId);

            // Load PackageResource entity from database
            var entity = await fhirDbContext.PackageResources
                .AsNoTracking()
                .FirstOrDefaultAsync(pr => pr.PackageResourceId == input.PackageResourceId)
                .ConfigureAwait(false);

            if (entity == null)
            {
                var errorMessage = $"PackageResource {input.PackageResourceId} not found";
                _logger.LogError("PackageResource {PackageResourceId} not found", input.PackageResourceId);

                // Return failure output (do not throw - let orchestration continue)
                return new ImportTerminologyResourceOutput(
                    PackageResourceId: input.PackageResourceId,
                    Canonical: "unknown",
                    ResourceType: "unknown",
                    Success: false,
                    ConceptCount: 0,
                    ErrorMessage: errorMessage);
            }

            // Map entity to domain model
            var packageResource = MapEntityToModel(entity);

            _logger.LogDebug(
                "Loaded PackageResource: {Canonical} ({ResourceType}) from package {PackageId}@{PackageVersion}",
                packageResource.Canonical,
                packageResource.ResourceType,
                packageResource.PackageId,
                packageResource.PackageVersion);

            // Update status to InProgress
            await UpdateImportStatusAsync(
                fhirDbContext,
                input.PackageResourceId,
                TerminologyImportStatus.InProgress,
                importStartDate: DateTimeOffset.UtcNow,
                cancellationToken: CancellationToken.None).ConfigureAwait(false);

            // Route to appropriate importer based on ResourceType
            TerminologyImportResult result;
            try
            {
                result = packageResource.ResourceType switch
                {
                    "CodeSystem" => await terminologyImporter.ImportCodeSystemAsync(input.TenantId, packageResource, CancellationToken.None).ConfigureAwait(false),
                    "ValueSet" => await terminologyImporter.ImportValueSetAsync(input.TenantId, packageResource, CancellationToken.None).ConfigureAwait(false),
                    "ConceptMap" => await terminologyImporter.ImportConceptMapAsync(input.TenantId, packageResource, CancellationToken.None).ConfigureAwait(false),
                    _ => throw new InvalidOperationException($"Unsupported ResourceType for terminology import: {packageResource.ResourceType}")
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error importing {ResourceType} {Canonical}: {Message}",
                    packageResource.ResourceType,
                    packageResource.Canonical,
                    ex.Message);

                // Update status to Failed
                await UpdateImportStatusAsync(
                    fhirDbContext,
                    input.PackageResourceId,
                    TerminologyImportStatus.Failed,
                    errorMessage: ex.Message,
                    importCompletedDate: DateTimeOffset.UtcNow,
                    cancellationToken: CancellationToken.None).ConfigureAwait(false);

                // Return failure output (do not throw)
                return new ImportTerminologyResourceOutput(
                    PackageResourceId: input.PackageResourceId,
                    Canonical: packageResource.Canonical,
                    ResourceType: packageResource.ResourceType,
                    Success: false,
                    ConceptCount: 0,
                    ErrorMessage: ex.Message);
            }

            // Update final status based on result
            await UpdateImportStatusAsync(
                fhirDbContext,
                input.PackageResourceId,
                result.Status,
                errorMessage: result.ErrorMessage,
                importCompletedDate: DateTimeOffset.UtcNow,
                importedConceptCount: result.ItemCount,
                cancellationToken: CancellationToken.None).ConfigureAwait(false);

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
        catch (Exception ex)
        {
            // Catch-all for unexpected errors
            _logger.LogError(
                ex,
                "Unexpected error in ImportTerminologyResourceActivity for PackageResourceId {PackageResourceId}",
                input.PackageResourceId);

            return new ImportTerminologyResourceOutput(
                PackageResourceId: input.PackageResourceId,
                Canonical: "unknown",
                ResourceType: "unknown",
                Success: false,
                ConceptCount: 0,
                ErrorMessage: ex.Message);
        }
    }

    /// <summary>
    /// Updates PackageResource import status fields in database.
    /// Uses raw SQL to avoid tracking conflicts.
    /// </summary>
    private async Task UpdateImportStatusAsync(
        FhirDbContext context,
        long packageResourceId,
        TerminologyImportStatus status,
        DateTimeOffset? importStartDate = null,
        DateTimeOffset? importCompletedDate = null,
        string? errorMessage = null,
        int? importedConceptCount = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Execute raw SQL update to avoid loading entity into DbContext
            // This prevents tracking conflicts and is more efficient for status updates
            var sql = @"
                UPDATE dbo.PackageResource
                SET TerminologyImportStatus = {0}";

            var parameters = new List<object> { status.ToString() };
            var paramIndex = 1;

            if (importStartDate.HasValue)
            {
                sql += $", ImportStartDate = {{{paramIndex}}}";
                parameters.Add(importStartDate.Value);
                paramIndex++;
            }

            if (importCompletedDate.HasValue)
            {
                sql += $", ImportCompletedDate = {{{paramIndex}}}";
                parameters.Add(importCompletedDate.Value);
                paramIndex++;
            }

            if (errorMessage != null)
            {
                sql += $", ImportErrorMessage = {{{paramIndex}}}";
                parameters.Add(errorMessage.Length > 1000 ? errorMessage.Substring(0, 1000) : errorMessage);
                paramIndex++;
            }

            if (importedConceptCount.HasValue)
            {
                sql += $", ImportedConceptCount = {{{paramIndex}}}";
                parameters.Add(importedConceptCount.Value);
                paramIndex++;
            }

            sql += $" WHERE PackageResourceId = {{{paramIndex}}}";
            parameters.Add(packageResourceId);

            await context.Database.ExecuteSqlRawAsync(sql, parameters, cancellationToken).ConfigureAwait(false);

            _logger.LogDebug(
                "Updated PackageResource {PackageResourceId} import status to {Status}",
                packageResourceId,
                status);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to update import status for PackageResourceId {PackageResourceId}",
                packageResourceId);
            // Don't throw - status update failure shouldn't block import
        }
    }

    /// <summary>
    /// Maps PackageResourceEntity to PackageResource domain model.
    /// </summary>
    private static PackageResource MapEntityToModel(Ignixa.DataLayer.SqlEntityFramework.Entities.PackageResourceEntity entity)
    {
        return new PackageResource
        {
            PackageResourceId = entity.PackageResourceId,
            PackageId = entity.PackageId,
            PackageVersion = entity.PackageVersion,
            ResourceType = entity.ResourceType,
            Canonical = entity.Canonical,
            Version = entity.Version,
            ResourceId = entity.ResourceId,
            ResourceJson = entity.ResourceJson,
            FhirVersion = entity.FhirVersion,
            LoadedDate = entity.LoadedDate,
            IsActive = entity.IsActive,
            TerminologyImportStatus = ParseTerminologyImportStatus(entity.TerminologyImportStatus),
            ContentHash = entity.ContentHash,
            ImportStartDate = entity.ImportStartDate,
            ImportCompletedDate = entity.ImportCompletedDate,
            ImportErrorMessage = entity.ImportErrorMessage,
            ImportedConceptCount = entity.ImportedConceptCount
        };
    }

    /// <summary>
    /// Parses TerminologyImportStatus from string (database stores as varchar).
    /// </summary>
    private static TerminologyImportStatus? ParseTerminologyImportStatus(string? statusString)
    {
        if (string.IsNullOrEmpty(statusString))
        {
            return null;
        }

        return Enum.TryParse<TerminologyImportStatus>(statusString, out var status)
            ? status
            : null;
    }
}
