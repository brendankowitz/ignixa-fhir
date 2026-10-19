// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using DurableTask.Core;
using Ignixa.Abstractions;
using Ignixa.Application.BackgroundOperations.BulkDelete.Orchestrations;
using Ignixa.Application.Features.Search;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Constants;
using Ignixa.Domain.Exceptions;
using Ignixa.Domain.Models;
using Ignixa.Search.Indexing;
using Ignixa.Search.Parsing;
using Ignixa.Serialization;
using Medino;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ignixa.Application.BackgroundOperations.BulkDelete;

/// <summary>
/// Validates a <c>$bulk-delete</c> request, snapshots its resource-type set and starts the orchestration.
/// </summary>
/// <remarks>
/// Validation is deliberately stricter than search: bulk delete is destructive, and every search parameter
/// the server would ignore widens the set of resources deleted. So result-shaping parameters, unknown
/// parameters and unsupported modifiers are 400s, and at system level every filter must be valid for every
/// targeted type.
/// </remarks>
public sealed class CreateBulkDeleteJobHandler(
    TaskHubClient taskHubClient,
    IBackgroundJobRepository<BulkDeleteJobDefinition> jobRepository,
    ITenantConfigurationStore tenantConfigurationStore,
    IFhirVersionContext fhirVersionContext,
    IFhirRepositoryFactory repositoryFactory,
    ISearchOptionsBuilderFactory searchOptionsBuilderFactory,
    IOptions<BulkDeleteOptions> options,
    ILogger<CreateBulkDeleteJobHandler> logger) : IRequestHandler<CreateBulkDeleteJobCommand, CreateBulkDeleteJobResult>
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private static readonly HashSet<ParameterCategory> ResultShapingCategories =
    [
        ParameterCategory.ContinuationToken,
        ParameterCategory.Count,
        ParameterCategory.Total,
        ParameterCategory.Summary,
        ParameterCategory.Sort,
        ParameterCategory.Elements,
        ParameterCategory.IncludesCount,
        ParameterCategory.IncludesContinuationToken,
    ];

    public async Task<CreateBulkDeleteJobResult> HandleAsync(
        CreateBulkDeleteJobCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // TenantResolutionMiddleware already rejects /tenant/0; this keeps the reserved system partition
        // unreachable for any other caller of the command.
        if (request.TenantId == SystemConstants.SystemPartitionId)
        {
            throw new BadRequestException(
                $"Bulk delete is not allowed on the system partition (tenant {SystemConstants.SystemPartitionId}).");
        }

        RejectEmptyValues(request.SearchParameters);
        var parameters = request.SearchParameters
            .Select(parameter => new QueryParameter(parameter.Key, parameter.Value))
            .ToList();
        RejectResultShapingParameters(parameters);

        if (request.RemoveReferences && request.Mode != BulkDeleteMode.HardDelete)
        {
            throw new BadRequestException("_remove-references requires _hardDelete=true.");
        }

        var tenant = await tenantConfigurationStore.GetTenantConfigurationAsync(request.TenantId, cancellationToken)
            ?? throw new InvalidOperationException($"Tenant {request.TenantId} not found or inactive");
        var version = FhirSpecificationExtensions.FromVersionString(tenant.FhirVersion);
        var concreteTypes = GetConcreteResourceTypes(fhirVersionContext.GetSchemaProvider(version, request.TenantId));

        var excluded = request.ExcludedResourceTypes.ToHashSet(StringComparer.Ordinal);
        RequireKnownTypes(excluded, concreteTypes, "excludedResourceTypes");
        var resourceTypes = ResolveResourceTypes(request, parameters, concreteTypes, excluded);

        var repository = await repositoryFactory.GetRepositoryAsync(request.TenantId, cancellationToken);
        if (request.Mode != BulkDeleteMode.SoftDelete && !repository.SupportsPhysicalDeletion)
        {
            throw new BadRequestException(
                $"Bulk delete with {(request.Mode == BulkDeleteMode.HardDelete ? "_hardDelete" : "_purgeHistory")} " +
                "is not supported by this tenant's storage provider.");
        }

        var includeParameters = parameters
            .Where(parameter => parameter.Category is ParameterCategory.Include or ParameterCategory.RevInclude)
            .Select(parameter => parameter.Name)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (includeParameters.Count > 0 && !StorageEvaluatesIncludes(repository))
        {
            throw new BadRequestException(
                $"Bulk delete with {Quote(includeParameters)} is not supported by this tenant's storage provider.");
        }

        var searchParameters = parameters
            .Where(parameter => parameter.Category is not (ParameterCategory.Type or ParameterCategory.Formatting))
            .ToList();
        ValidateSearch(version, request.TenantId, resourceTypes, searchParameters);
        var searchQuery = EncodeSearchQuery(searchParameters);

        var jobId = Guid.NewGuid().ToString();
        var now = DateTimeOffset.UtcNow;
        var job = new BackgroundJob<BulkDeleteJobDefinition>
        {
            JobId = jobId,
            OrchestrationInstanceId = jobId,
            JobType = (int)BackgroundJobType.BulkDelete,
            Status = "Queued",
            Definition = new BulkDeleteJobDefinition
            {
                TenantId = request.TenantId,
                ResourceTypes = resourceTypes,
                SearchQuery = searchQuery,
                Mode = request.Mode,
                ExcludedResourceTypes = [.. excluded.Order(StringComparer.Ordinal)],
                RemoveReferences = request.RemoveReferences,
                IsSystemLevel = request.ResourceType is null,
                RequestUrl = request.RequestUrl,
            },
            CreateDate = now,
            HeartbeatDate = now,
        };
        await jobRepository.CreateAsync(job, cancellationToken);

        var input = new BulkDeleteOrchestrationInput(
            JobId: jobId,
            TenantId: request.TenantId,
            ResourceTypes: resourceTypes,
            SearchQuery: searchQuery,
            Mode: request.Mode,
            ExcludedResourceTypes: job.Definition.ExcludedResourceTypes,
            RemoveReferences: request.RemoveReferences,
            BatchSize: options.Value.BatchSize);
        await StartOrchestrationAsync(job, input);

        logger.LogInformation(
            "Started bulk delete job {JobId} for tenant {TenantId}: mode {Mode}, {TypeCount} resource type(s), batch size {BatchSize}",
            jobId, request.TenantId, request.Mode, resourceTypes.Count, input.BatchSize);
        return new CreateBulkDeleteJobResult(jobId);
    }

    /// <summary>
    /// Starts the orchestration. A job row whose orchestration never started would report Queued forever,
    /// so a start failure marks the job Failed before the original exception propagates.
    /// </summary>
    private async Task StartOrchestrationAsync(BackgroundJob<BulkDeleteJobDefinition> job, BulkDeleteOrchestrationInput input)
    {
        try
        {
            await taskHubClient.CreateOrchestrationInstanceAsync(typeof(BulkDeleteOrchestration), job.JobId, input);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Bulk delete job {JobId} could not start its orchestration", job.JobId);
            job.Status = "Failed";
            job.EndDate = DateTimeOffset.UtcNow;
            job.ErrorMessage = $"The bulk delete orchestration could not be started: {ex.Message}";
            job.Result = JsonSerializer.SerializeToNode(
                new BulkDeleteJobResult { Issues = [job.ErrorMessage] }, SerializerOptions);
            // Not the request token: the row must not be left Queued because the caller went away.
            await jobRepository.UpdateAsync(job, job.Definition.TenantId, CancellationToken.None);
            throw;
        }
    }

    /// <summary>
    /// The HTTP query parser drops parameters with empty values. For a search that only narrows the result,
    /// but here a dropped filter widens what is deleted: <c>?_type=</c> would delete every type and
    /// <c>?identifier=</c> every resource of the type.
    /// </summary>
    private static void RejectEmptyValues(IReadOnlyList<KeyValuePair<string, string>> parameters)
    {
        var empty = parameters
            .Where(parameter => string.IsNullOrWhiteSpace(parameter.Value))
            .Select(parameter => parameter.Key)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (empty.Count > 0)
        {
            throw new BadRequestException($"Bulk delete search parameter(s) {Quote(empty)} have no value.");
        }
    }

    /// <summary>
    /// Whether the tenant's search provider evaluates <c>_include</c>/<c>_revinclude</c>. No provider
    /// declares this, so physical-deletion support stands in for "SQL-class provider": the SQL provider
    /// evaluates includes and supports physical deletion, while the file-system prototype does neither -- it
    /// returns no include entries at all, so the cascade would silently not happen. Replace this with a
    /// search-provider capability when one exists.
    /// </summary>
    private static bool StorageEvaluatesIncludes(IFhirRepository repository) => repository.SupportsPhysicalDeletion;

    private static void RejectResultShapingParameters(IReadOnlyList<QueryParameter> parameters)
    {
        var rejected = parameters
            .Where(parameter => ResultShapingCategories.Contains(parameter.Category) ||
                parameter.Name is "_contained" or "_containedType")
            .Select(parameter => parameter.Name)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (rejected.Count > 0)
        {
            throw new BadRequestException(
                $"Bulk delete does not support the result parameter(s) {Quote(rejected)}.");
        }
    }

    private static HashSet<string> GetConcreteResourceTypes(IFhirSchemaProvider schema) =>
        schema.ResourceTypeNames
            .Where(type => !(schema.GetTypeDefinition(type)
                ?? throw new InvalidOperationException($"Tenant schema declares '{type}' without a type definition.")).Info.IsAbstract)
            .ToHashSet(StringComparer.Ordinal);

    private static void RequireKnownTypes(IEnumerable<string> types, HashSet<string> concreteTypes, string source)
    {
        var unknown = types.Where(type => !concreteTypes.Contains(type)).ToList();
        if (unknown.Count > 0)
        {
            throw new BadRequestException($"{source} names unsupported resource type(s) {Quote(unknown)}.");
        }
    }

    private static IReadOnlyList<string> ResolveResourceTypes(
        CreateBulkDeleteJobCommand request,
        IReadOnlyList<QueryParameter> parameters,
        HashSet<string> concreteTypes,
        HashSet<string> excluded)
    {
        var typeParameters = parameters.Where(parameter => parameter.Category == ParameterCategory.Type).ToList();

        if (request.ResourceType is { } resourceType)
        {
            RequireKnownTypes([resourceType], concreteTypes, "The route");
            if (typeParameters.Count > 0)
            {
                throw new BadRequestException("_type is not allowed when bulk deleting a single resource type.");
            }

            if (excluded.Contains(resourceType))
            {
                throw new BadRequestException($"excludedResourceTypes excludes the target resource type '{resourceType}'.");
            }

            return [resourceType];
        }

        var requested = typeParameters
            .SelectMany(parameter => parameter.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToHashSet(StringComparer.Ordinal);
        if (typeParameters.Count > 0 && requested.Count == 0)
        {
            throw new BadRequestException("_type must name at least one resource type.");
        }

        RequireKnownTypes(requested, concreteTypes, "_type");
        var selected = concreteTypes
            .Where(type => (requested.Count == 0 || requested.Contains(type)) && !excluded.Contains(type))
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (selected.Length == 0)
        {
            throw new BadRequestException("No resource types remain to delete after applying _type and excludedResourceTypes.");
        }

        return selected;
    }

    private void ValidateSearch(
        FhirVersion version,
        int tenantId,
        IReadOnlyList<string> resourceTypes,
        IReadOnlyList<QueryParameter> searchParameters)
    {
        var builder = searchOptionsBuilderFactory.Create(version, tenantId);
        foreach (var resourceType in resourceTypes)
        {
            var searchOptions = builder.Build(resourceType, searchParameters);
            SearchModifierNotSupportedException.ThrowIfAny(searchOptions);
            if (searchOptions.UnsupportedParams.Count > 0)
            {
                throw new BadRequestException(
                    $"Bulk delete does not support the search parameter(s) {Quote(searchOptions.UnsupportedParams)} " +
                    $"for resource type '{resourceType}'. Ignoring them would widen the deletion.");
            }
        }
    }

    /// <summary>
    /// Encodes the filters for <see cref="IQueryParameterParser.Parse(string)"/>, which splits on '&amp;' and the
    /// first '=' and unescapes values but not names. Values are therefore escaped and names kept verbatim
    /// (escaping would corrupt names such as <c>_include:iterate</c>); a name that would change meaning
    /// when split is rejected.
    /// </summary>
    private static string EncodeSearchQuery(IReadOnlyList<QueryParameter> searchParameters)
    {
        var unencodable = searchParameters
            .Select(parameter => parameter.Name)
            .Where(name => name.Length == 0 || name.Contains('&', StringComparison.Ordinal) ||
                name.Contains('=', StringComparison.Ordinal) || name.StartsWith('?'))
            .ToList();
        if (unencodable.Count > 0)
        {
            throw new BadRequestException($"Bulk delete does not support the search parameter(s) {Quote(unencodable)}.");
        }

        return string.Join('&', searchParameters.Select(parameter => $"{parameter.Name}={Uri.EscapeDataString(parameter.Value)}"));
    }

    private static string Quote(IEnumerable<string> values) => string.Join(", ", values.Select(value => $"'{value}'"));
}
