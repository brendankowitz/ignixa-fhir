// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Nodes;
using DurableTask.Core;
using Ignixa.Abstractions;
using Ignixa.Application.BackgroundOperations.BulkDelete.Models;
using Ignixa.Application.Features.Resource;
using Ignixa.Application.Features.Search;
using Ignixa.Application.Infrastructure;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Exceptions;
using Ignixa.Domain.Models;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Parsing;
using Ignixa.Serialization;
using Medino;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.BackgroundOperations.BulkDelete.Activities;

/// <summary>
/// Deletes one page of a bulk-delete job's matches for one resource type, together with its
/// <c>_include</c>/<c>_revinclude</c> cascade, then persists cumulative progress.
/// </summary>
/// <remarks>
/// <para>
/// Order follows microsoft/fhir-server: referrers are rewritten first (remove-references), then included
/// resources are deleted, then matches. A failure part-way leaves the matches in place, so the retried
/// batch finds the same page and resumes.
/// </para>
/// <para>
/// Cancellation is cooperative through the job row: a batch that finds the job terminal deletes nothing,
/// and a batch whose progress write loses to a terminal state reports <see cref="BulkDeleteBatchOutput.Superseded"/>.
/// </para>
/// </remarks>
public class BulkDeleteBatchActivity(
    IBackgroundJobRepository<BulkDeleteJobDefinition> jobRepository,
    ITenantConfigurationStore tenantConfigurationStore,
    ISearchServiceFactory searchServiceFactory,
    IFhirRepositoryFactory repositoryFactory,
    IQueryParameterParser parameterParser,
    ISearchOptionsBuilderFactory searchOptionsBuilderFactory,
    IFhirVersionContext fhirVersionContext,
    IFhirBaseUriProvider baseUriProvider,
    IMediator mediator,
    IFhirRequestContextAccessor fhirContextAccessor,
    IHostApplicationLifetime applicationLifetime,
    ILogger<BulkDeleteBatchActivity> logger) : AsyncTaskActivity<BulkDeleteBatchInput, BulkDeleteBatchOutput>
{
    /// <summary>
    /// Matches per <c>_id</c> search when reading a purge page's include cascade. Each id is a bound SQL
    /// parameter, and SQL Server allows about 2,100 per statement; the batch size may be up to 10,000.
    /// </summary>
    private const int CascadeSeedChunkSize = 100;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    protected override async Task<BulkDeleteBatchOutput> ExecuteAsync(TaskContext context, BulkDeleteBatchInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.RemoveReferences && input.Mode != BulkDeleteMode.HardDelete)
        {
            // Kickoff rejects this; reaching here means a corrupt or hand-built input. Rewriting referrers of
            // resources that are not physically deleted would strip references to live resources.
            throw new InvalidOperationException("Bulk delete can remove references only when hard deleting.");
        }

        // Activities receive no token; host shutdown is the only cancellation source. A stopped batch is
        // retried (or resumed after restart) from the job's persisted state.
        var cancellationToken = applicationLifetime.ApplicationStopping;

        var job = await BulkDeleteJobs.FindAsync(jobRepository, input.TenantId, input.JobId, cancellationToken)
            ?? throw new InvalidOperationException($"Bulk delete job {input.JobId} not found for tenant {input.TenantId}.");
        if (BulkDeleteJobs.IsTerminal(job.Status))
        {
            logger.LogInformation("Bulk delete batch for {JobId} skipped: job is {Status}", input.JobId, job.Status);
            return Superseded([]);
        }

        var tenant = await tenantConfigurationStore.GetTenantConfigurationAsync(input.TenantId, cancellationToken)
            ?? throw new InvalidOperationException($"Tenant {input.TenantId} not found or inactive");
        var version = FhirSpecificationExtensions.FromVersionString(tenant.FhirVersion);

        BulkDeletePage page;
        Dictionary<string, long> deleted;
        var previousContext = fhirContextAccessor.RequestContext;
        try
        {
            fhirContextAccessor.RequestContext = FhirRequestContextFactory.CreateBackgroundContext(
                input.TenantId, tenant, version, input.ResourceType);
            var searchService = await searchServiceFactory.GetSearchServiceAsync(input.TenantId, cancellationToken);
            var builder = searchOptionsBuilderFactory.Create(version, input.TenantId);

            page = await ReadPageAsync(searchService, builder, input, cancellationToken);
            if (input.RemoveReferences)
            {
                // Built here, inside the background request context, so the provider answers over the
                // service base URIs of this job's tenant -- and from the same (schema, base URI provider)
                // pair the indexer is built from, which is what makes removal recognize every reference
                // form the index collapsed onto the target.
                var referenceParser = new ReferenceSearchValueParser(
                    fhirVersionContext.GetSchemaProvider(version, input.TenantId), baseUriProvider);
                await RemoveReferencesAsync(searchService, builder, page.Targets, referenceParser, cancellationToken);
            }

            deleted = await DeleteAsync(input, page.Targets, cancellationToken);
        }
        finally
        {
            fhirContextAccessor.RequestContext = previousContext;
        }

        logger.LogInformation(
            "Bulk delete batch for job {JobId}, tenant {TenantId}, type {ResourceType} ({Mode}): {MatchCount} match(es), " +
            "{IncludeCount} include(s), deleted {@DeletedCounts}, more: {HasMore}",
            input.JobId, input.TenantId, input.ResourceType, input.Mode, page.MatchCount,
            page.Targets.Count - page.MatchCount, deleted, page.HasMore);

        if (!await PersistProgressAsync(input, deleted, cancellationToken))
        {
            return Superseded(deleted);
        }

        return new BulkDeleteBatchOutput(deleted, page.HasMore, page.NextContinuationToken, page.FirstMatchKey, Superseded: false);
    }

    private async Task<BulkDeletePage> ReadPageAsync(
        ISearchService searchService,
        ISearchOptionsBuilder builder,
        BulkDeleteBatchInput input,
        CancellationToken cancellationToken)
    {
        var parameters = parameterParser.Parse(input.SearchQuery);
        var options = BuildSearch(builder, input.ResourceType, parameters);
        options.MaxItemCount = input.BatchSize;
        options.ProbeExtraRow = true;
        options.Sort = [];
        var cascadeSeparately = ConfigurePaging(options, input);

        var excluded = input.ExcludedResourceTypes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var matches = new List<SearchEntryResult>();
        var includes = new List<SearchEntryResult>();
        var probeFound = false;
        string? probeToken = null;
        await foreach (var entry in searchService.SearchStreamAsync(options, cancellationToken))
        {
            if (entry.IsPagingProbe)
            {
                probeFound = true;
                probeToken ??= entry.ContinuationToken;
                continue;
            }

            if (IsMatch(entry, input.ResourceType))
            {
                matches.Add(entry);
            }
            else if (!excluded.Contains(entry.ResourceType))
            {
                includes.Add(entry);
            }
        }

        // Includes are deleted before matches; a resource that is both counts once, as a match.
        var distinctMatches = matches.DistinctBy(Key, StringComparer.Ordinal).ToList();
        if (cascadeSeparately && distinctMatches.Count > 0)
        {
            includes.AddRange(await ReadCascadeAsync(searchService, builder, input.ResourceType, parameters, distinctMatches, excluded, cancellationToken));
        }

        var matchKeys = distinctMatches.Select(Key).ToHashSet(StringComparer.Ordinal);
        var targets = includes
            .DistinctBy(Key, StringComparer.Ordinal)
            .Where(include => !matchKeys.Contains(Key(include)))
            .Concat(distinctMatches)
            .ToList();

        return new BulkDeletePage(
            targets,
            distinctMatches.Count,
            probeFound,
            probeFound ? NextContinuationToken(input, probeToken) : null,
            matches.Count > 0 ? Key(matches[0]) : null);
    }

    private static SearchOptions BuildSearch(ISearchOptionsBuilder builder, string resourceType, IReadOnlyList<QueryParameter> parameters)
    {
        var options = builder.Build(resourceType, parameters);
        // Kickoff rejected these; the tenant's search parameters may have changed since. Fail rather than
        // run a widened delete.
        SearchModifierNotSupportedException.ThrowIfAny(options);
        if (options.UnsupportedParams.Count > 0)
        {
            throw new InvalidOperationException(
                $"Search parameter(s) {string.Join(", ", options.UnsupportedParams)} are no longer supported for '{resourceType}'.");
        }

        return options;
    }

    /// <summary>
    /// Restart mode (soft/hard) always reads the first page, includes and all: deleted matches drop out, so
    /// the next batch's first page is the next work. Purge keeps every current version, so it pages forward
    /// over the matches with the provider's keyset cursor. That cursor cannot carry includes, and an offset
    /// cursor is unsafe (an update during the run gives a resource a new position and shifts every later
    /// page), so purge strips the includes here and reads the cascade per page instead.
    /// </summary>
    /// <returns>True when the include cascade must be read separately for the page's matches.</returns>
    private static bool ConfigurePaging(SearchOptions options, BulkDeleteBatchInput input)
    {
        if (input.Mode != BulkDeleteMode.PurgeHistory)
        {
            if (input.ContinuationToken is not null)
            {
                throw new InvalidOperationException("Soft and hard delete batches always read the first page.");
            }

            options.ContinuationToken = null;
            return false;
        }

        var hasIncludes = options.Include.Count > 0 || options.RevInclude.Count > 0;
        options.Include = [];
        options.RevInclude = [];
        options.UseExportContinuation = true;
        options.ContinuationToken = input.ContinuationToken;
        return hasIncludes;
    }

    /// <summary>
    /// Reads the <c>_include</c>/<c>_revinclude</c> cascade of a page of matches by searching for exactly
    /// those matches (<c>_id</c>) with the original include parameters. Seeds are chunked to bound the
    /// number of SQL parameters a single search binds.
    /// </summary>
    private static async Task<List<SearchEntryResult>> ReadCascadeAsync(
        ISearchService searchService,
        ISearchOptionsBuilder builder,
        string resourceType,
        IReadOnlyList<QueryParameter> parameters,
        IReadOnlyList<SearchEntryResult> matches,
        HashSet<string> excluded,
        CancellationToken cancellationToken)
    {
        var includeParameters = parameters
            .Where(parameter => parameter.Category is ParameterCategory.Include or ParameterCategory.RevInclude)
            .ToList();
        var includes = new List<SearchEntryResult>();
        foreach (var seeds in matches.Chunk(CascadeSeedChunkSize))
        {
            var options = BuildSearch(builder, resourceType,
                [new QueryParameter("_id", string.Join(',', seeds.Select(seed => seed.ResourceId))), .. includeParameters]);
            options.MaxItemCount = seeds.Length;
            options.ProbeExtraRow = false;
            options.Sort = [];
            await foreach (var entry in searchService.SearchStreamAsync(options, cancellationToken))
            {
                if (!entry.IsPagingProbe && !IsMatch(entry, resourceType) && !excluded.Contains(entry.ResourceType))
                {
                    includes.Add(entry);
                }
            }
        }

        return includes;
    }

    /// <returns>True for a match, false for an include.</returns>
    private static bool IsMatch(SearchEntryResult entry, string resourceType) =>
        entry.SearchMode switch
        {
            SearchEntryMode.Match => true,
            SearchEntryMode.Include => false,
            // An outcome entry stands for a resource that could not be read; deleting around it would
            // report a complete job while it remains.
            _ => throw new InvalidOperationException(
                $"The {resourceType} search returned an unreadable resource; the batch cannot be processed."),
        };

    private static string? NextContinuationToken(BulkDeleteBatchInput input, string? probeToken)
    {
        if (input.Mode != BulkDeleteMode.PurgeHistory)
        {
            return null;
        }

        var next = probeToken
            ?? throw new InvalidOperationException("The search provider returned a paging probe without a continuation.");
        return next == input.ContinuationToken
            ? throw new InvalidOperationException("The search provider returned a non-advancing continuation.")
            : next;
    }

    /// <summary>
    /// Rewrites every resource that references a target and is not itself being deleted. A referrer of
    /// several targets is rewritten once, conditional on the version that was read, so a concurrent writer
    /// fails the batch (and its retry re-reads) instead of being overwritten.
    /// </summary>
    private async Task RemoveReferencesAsync(
        ISearchService searchService,
        ISearchOptionsBuilder builder,
        IReadOnlyList<SearchEntryResult> targets,
        IReferenceSearchValueParser referenceParser,
        CancellationToken cancellationToken)
    {
        var targetKeys = targets.Select(Key).ToHashSet(StringComparer.Ordinal);
        var referrers = new Dictionary<string, (SearchEntryResult Entry, List<string> Targets)>(StringComparer.Ordinal);
        foreach (var target in targets)
        {
            var options = builder.Build(target.ResourceType, parameterParser.Parse(
                $"_id={Uri.EscapeDataString(target.ResourceId)}&_revinclude={Uri.EscapeDataString("*:*")}"));
            options.Sort = [];
            await foreach (var entry in searchService.SearchStreamAsync(options, cancellationToken))
            {
                if (entry.IsPagingProbe || entry.SearchMode != SearchEntryMode.Include || targetKeys.Contains(Key(entry)))
                {
                    continue;
                }

                if (!referrers.TryGetValue(Key(entry), out var referrer))
                {
                    referrer = (entry, []);
                    referrers.Add(Key(entry), referrer);
                }

                referrer.Targets.Add(Key(target));
            }
        }

        foreach (var (entry, referencedTargets) in referrers.Values)
        {
            var node = JsonNode.Parse(entry.ResourceBytes.Span)
                ?? throw new InvalidOperationException($"Referrer {Key(entry)} has an empty body.");
            var changed = false;
            foreach (var target in referencedTargets)
            {
                changed |= BulkDeleteReferenceRemover.RemoveReferences(node, target, referenceParser);
            }

            if (!changed)
            {
                // The search found this referrer through the reference index but nothing in its body
                // resolved to the target. Benign when the only link is one the index records and the body
                // does not carry as a "reference" (an identifier-only or contained link); otherwise it is a
                // reference the index normalized and removal failed to recognize, and the target is about
                // to be hard deleted. Either way it leaves the referrer pointing at a deleted resource, so
                // it must not pass silently.
                logger.LogWarning(
                    "Bulk delete found {Referrer} referring to {Targets} but removed no reference from it; " +
                    "the referrer is left pointing at a resource this job deletes",
                    Key(entry), referencedTargets);
                continue;
            }

            await mediator.SendAsync(new CreateOrUpdateResourceCommand(
                entry.ResourceType,
                entry.ResourceId,
                JsonSourceNodeFactory.Parse(node),
                HttpMethod.Put,
                IfMatch: entry.VersionId), cancellationToken);
            logger.LogInformation(
                "Bulk delete removed references to {Targets} from {Referrer}", referencedTargets, Key(entry));
        }
    }

    private async Task<Dictionary<string, long>> DeleteAsync(
        BulkDeleteBatchInput input,
        IReadOnlyList<SearchEntryResult> targets,
        CancellationToken cancellationToken)
    {
        var repository = await repositoryFactory.GetRepositoryAsync(input.TenantId, cancellationToken);
        var counts = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var target in targets)
        {
            var key = new ResourceKey(target.ResourceType, target.ResourceId, null, input.TenantId);
            if (await DeleteOneAsync(repository, input.Mode, key, cancellationToken))
            {
                counts[target.ResourceType] = counts.GetValueOrDefault(target.ResourceType) + 1;
            }
        }

        return counts;
    }

    /// <returns>Whether the resource counts as deleted.</returns>
    private async Task<bool> DeleteOneAsync(
        IFhirRepository repository,
        BulkDeleteMode mode,
        ResourceKey key,
        CancellationToken cancellationToken)
    {
        switch (mode)
        {
            case BulkDeleteMode.SoftDelete:
                return await mediator.SendAsync(new DeleteResourceCommand(key.ResourceType, key.Id), cancellationToken);
            case BulkDeleteMode.HardDelete:
                return await repository.HardDeleteAsync(key, cancellationToken);
            case BulkDeleteMode.PurgeHistory:
                // fhir-server counts resources processed, not versions removed.
                await repository.PurgeHistoryAsync(key, cancellationToken);
                return true;
            default:
                throw new InvalidOperationException($"Unknown bulk delete mode {mode}.");
        }
    }

    /// <returns>False when the job reached a terminal state first; this batch's deletions stand but are not recorded.</returns>
    private async Task<bool> PersistProgressAsync(
        BulkDeleteBatchInput input,
        Dictionary<string, long> deleted,
        CancellationToken cancellationToken)
    {
        var job = await BulkDeleteJobs.FindAsync(jobRepository, input.TenantId, input.JobId, cancellationToken)
            ?? throw new InvalidOperationException($"Bulk delete job {input.JobId} disappeared during a batch.");
        if (BulkDeleteJobs.IsTerminal(job.Status))
        {
            logger.LogInformation("Bulk delete progress for {JobId} superseded by {Status}", input.JobId, job.Status);
            return false;
        }

        var cumulative = new Dictionary<string, long>(input.CumulativeCounts, StringComparer.Ordinal);
        foreach (var (type, count) in deleted)
        {
            cumulative[type] = cumulative.GetValueOrDefault(type) + count;
        }

        var now = DateTimeOffset.UtcNow;
        job.Status = "Running";
        job.StartDate ??= now;
        job.HeartbeatDate = now;
        job.Progress = JsonSerializer.SerializeToNode(
            new BulkDeleteJobProgress { ResourceDeletedCount = cumulative }, SerializerOptions);
        try
        {
            await jobRepository.UpdateAsync(job, input.TenantId, cancellationToken);
            return true;
        }
        catch (BackgroundJobUpdateConflictException conflict)
        {
            logger.LogInformation("Bulk delete progress for {JobId} lost to {Status}", input.JobId, conflict.CurrentStatus);
            return false;
        }
    }

    private static BulkDeleteBatchOutput Superseded(Dictionary<string, long> deleted) =>
        new(deleted, HasMore: false, NextContinuationToken: null, FirstMatchKey: null, Superseded: true);

    private static string Key(SearchEntryResult entry) => $"{entry.ResourceType}/{entry.ResourceId}";
}
