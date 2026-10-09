using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Ignixa.Conformance.Events;
using Ignixa.Conformance.Events.Abstractions;
using Ignixa.Conformance.Events.Events;
using Ignixa.Conformance.Events.Models;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.Features.Conformance;

public sealed class ConformanceState : IConformanceStateView, IDisposable
{
    private readonly Dictionary<(string ResourceType, string Code), ActiveSearchParameter> _searchParameters = [];
    private readonly List<ActiveSearchParameter> _searchParameterActivations = [];
    private readonly Dictionary<string, ActiveStructureDefinition> _structureDefinitions = [];
    private readonly Dictionary<string, ActivePackage> _packages = [];
    private readonly ConcurrentDictionary<string, string> _storageCanonicals = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _canonicalToParamId = [];
    private readonly SemaphoreSlim _activationLock = new(1, 1);
    private readonly ILogger<ConformanceState>? _logger;

    private int _nextSearchParamId = 1;
    private long _lastProcessedEventId;
    private volatile bool _isInitialized;

    public ConformanceState()
    {
    }

    public ConformanceState(ILogger<ConformanceState> logger)
    {
        _logger = logger;
    }

    public long LastProcessedEventId => Interlocked.Read(ref _lastProcessedEventId);
    public bool IsInitialized => _isInitialized;

    public async Task<IDisposable> AcquireActivationLockAsync(
        CancellationToken cancellationToken,
        [CallerMemberName] string operation = "")
    {
        var waitStarted = Stopwatch.GetTimestamp();
        await _activationLock.WaitAsync(cancellationToken);
        var acquired = Stopwatch.GetTimestamp();
        _logger?.LogDebug(
            "Conformance activation lock acquired by {Operation} after waiting {WaitDurationMs:N1} ms",
            operation,
            Stopwatch.GetElapsedTime(waitStarted, acquired).TotalMilliseconds);
        return new LockReleaser(_activationLock, _logger, operation, acquired);
    }

    public int GetOrAllocateSearchParamId(string canonical, ActiveSearchParameter? existingOverride)
    {
        if (existingOverride is not null)
        {
            _canonicalToParamId[canonical] = existingOverride.SearchParamId;
            return existingOverride.SearchParamId;
        }

        if (_canonicalToParamId.TryGetValue(canonical, out var existing))
            return existing;

        var newId = Interlocked.Increment(ref _nextSearchParamId) - 1;
        _canonicalToParamId[canonical] = newId;
        return newId;
    }

    // Selection does not mutate bindings. Applying the validated event advances allocation,
    // matching durable replay instead of changing the state against which it is validated.
    internal int GetSearchParamIdForActivation(string canonical, ActiveSearchParameter? existingOverride) =>
        existingOverride?.SearchParamId
        ?? (_canonicalToParamId.TryGetValue(canonical, out var id) ? id : _nextSearchParamId);

    private sealed class LockReleaser(
        SemaphoreSlim semaphore,
        ILogger<ConformanceState>? logger,
        string operation,
        long acquired) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (!_disposed)
            {
                semaphore.Release();
                logger?.LogDebug(
                    "Conformance activation lock released by {Operation} after holding {HoldDurationMs:N1} ms",
                    operation,
                    Stopwatch.GetElapsedTime(acquired).TotalMilliseconds);
                _disposed = true;
            }
        }
    }

    public IEnumerable<ActiveSearchParameter> EnabledSearchParameters =>
        _searchParameters.Values.Where(sp => sp.Status == SearchParameterStatus.Enabled);

    public IReadOnlyDictionary<(string ResourceType, string Code), ActiveSearchParameter> AllSearchParameters => _searchParameters;
    public IReadOnlyDictionary<string, ActiveStructureDefinition> StructureDefinitions => _structureDefinitions;
    public IReadOnlyDictionary<string, ActivePackage> Packages => _packages;

    public ActiveSearchParameter? GetEnabledSearchParameter(string resourceType, string code)
    {
        if (_searchParameters.TryGetValue((resourceType, code), out var sp) &&
            sp.Status == SearchParameterStatus.Enabled)
        {
            return sp;
        }
        return null;
    }

    public ActiveSearchParameter? GetSearchParameter(string resourceType, string code) =>
        _searchParameters.GetValueOrDefault((resourceType, code));

    public ActiveSearchParameter? FindByCanonical(string canonical) =>
        _searchParameterActivations.LastOrDefault(sp => sp.Canonical == canonical);

    public ActiveSearchParameter? GetLatestNonDisabledActivation(string resourceType, string code) =>
        _searchParameterActivations.LastOrDefault(parameter =>
            parameter.ResourceType == resourceType &&
            parameter.Code == code &&
            parameter.IsAvailable &&
            parameter.Status != SearchParameterStatus.Disabled);

    public ActiveSearchParameter? FindExtractedByCanonical(string canonical) =>
        _searchParameters.Values.LastOrDefault(sp => sp.Canonical == canonical);

    public IReadOnlyList<SearchParameterTransitionCandidate> GetTransitionCandidates(long hideEventId) =>
        _searchParameterActivations
            .Where(parameter =>
                parameter.Status == SearchParameterStatus.Staged &&
                parameter.ActivationEventId == hideEventId ||
                parameter.Status == SearchParameterStatus.Disabling &&
                parameter.DeactivationEventId == hideEventId)
            .GroupBy(parameter => parameter.SearchParamId)
            .Select(group => new SearchParameterTransitionCandidate(
                group.Key,
                group.Where(parameter => parameter.Status == SearchParameterStatus.Staged)
                    .Select(parameter => parameter.ActivationEventId)
                    .Distinct()
                    .ToArray(),
                group.Where(parameter => parameter.Status == SearchParameterStatus.Disabling)
                    .Select(parameter => parameter.DeactivationEventId!.Value)
                    .Distinct()
                    .ToArray()))
            .ToArray();

    public IReadOnlyList<long> GetTransitionHideEventIds() =>
        _searchParameterActivations
            .Where(parameter => parameter.Status is SearchParameterStatus.Staged or SearchParameterStatus.Disabling)
            .SelectMany(parameter => new[] { parameter.ActivationEventId, parameter.DeactivationEventId })
            .OfType<long>()
            .Distinct()
            .ToArray();

    public bool TryGetSearchParameterStorageCanonical(string canonical, out string storageCanonical) =>
        _storageCanonicals.TryGetValue(canonical, out storageCanonical!);

    // The caller holds the activation lock. Allocation and proposed-event application must not
    // change published state until the complete batch has validated and the append has succeeded.
    internal ConformanceState CreateStagingCopy()
    {
        var staged = new ConformanceState
        {
            _nextSearchParamId = _nextSearchParamId,
            _lastProcessedEventId = _lastProcessedEventId,
            _isInitialized = _isInitialized
        };
        var clones = new Dictionary<ActiveSearchParameter, ActiveSearchParameter>();
        foreach (var parameter in _searchParameterActivations)
        {
            var clone = new ActiveSearchParameter
            {
                SearchParamId = parameter.SearchParamId,
                Canonical = parameter.Canonical,
                Code = parameter.Code,
                ResourceType = parameter.ResourceType,
                Expression = parameter.Expression,
                ParamType = parameter.ParamType,
                SourcePackage = parameter.SourcePackage,
                OverridesCanonical = parameter.OverridesCanonical,
                TargetResourceTypes = parameter.TargetResourceTypes?.ToArray(),
                Components = parameter.Components?.ToArray(),
                Name = parameter.Name,
                Description = parameter.Description,
                ActivationEventId = parameter.ActivationEventId,
                DeactivationEventId = parameter.DeactivationEventId,
                PreviousActivationEventId = parameter.PreviousActivationEventId,
                IsAvailable = parameter.IsAvailable,
                Status = parameter.Status,
                ReindexJobId = parameter.ReindexJobId
            };
            clones.Add(parameter, clone);
            staged._searchParameterActivations.Add(clone);
        }
        foreach (var (key, parameter) in _searchParameters)
        {
            staged._searchParameters.Add(key, clones[parameter]);
        }
        foreach (var (key, definition) in _structureDefinitions)
        {
            staged._structureDefinitions.Add(key, definition);
        }
        foreach (var (key, package) in _packages)
        {
            staged._packages.Add(key, package);
        }
        foreach (var (canonical, root) in _storageCanonicals)
        {
            staged._storageCanonicals[canonical] = root;
        }
        foreach (var (canonical, id) in _canonicalToParamId)
        {
            staged._canonicalToParamId.Add(canonical, id);
        }
        return staged;
    }

    internal ConformanceStateSnapshot CreateSnapshot() =>
        new(_searchParameters, _searchParameterActivations, _storageCanonicals, _isInitialized);

    internal ValidationIssue? ApplyProposedEvent(NewSourceEvent proposed)
    {
        if (proposed.Data is SearchParameterActivated parameter &&
            ValidateSearchParameterActivation(parameter, out _) is { } issue)
        {
            return issue;
        }

        Apply(new SourceEvent(0, proposed.StreamId, proposed.EventType, proposed.Data, DateTimeOffset.UtcNow));
        return null;
    }

    public async Task InitializeFromEventsAsync(
        ISourceEventStore store,
        CancellationToken cancellationToken)
    {
        using (await AcquireActivationLockAsync(cancellationToken))
        {
            if (_lastProcessedEventId == 0)
            {
                await foreach (var evt in store.ReadAllAsync(cancellationToken))
                {
                    Apply(evt);
                    _lastProcessedEventId = evt.EventId;
                }
            }
            else
            {
                await CatchUpWhileActivationLockHeldAsync(store, cancellationToken);
            }

            _isInitialized = true;
        }
    }

    public void ApplyAndTrack(SourceEvent evt)
    {
        Apply(evt);
        _lastProcessedEventId = evt.EventId;
    }

    public async Task ApplyEventsAsync(
        IEnumerable<SourceEvent> events,
        CancellationToken cancellationToken)
    {
        using (await AcquireActivationLockAsync(cancellationToken))
        {
            foreach (var evt in events)
            {
                Apply(evt);
                _lastProcessedEventId = evt.EventId;
            }
        }
    }

    public async Task CatchUpAsync(
        ISourceEventStore store,
        CancellationToken cancellationToken)
    {
        using (await AcquireActivationLockAsync(cancellationToken))
        {
            await CatchUpWhileActivationLockHeldAsync(store, cancellationToken);
        }
    }

    /// <summary>
    /// Applies events after the current projection position while the caller holds the activation lock.
    /// </summary>
    public async Task CatchUpWhileActivationLockHeldAsync(
        ISourceEventStore store,
        CancellationToken cancellationToken)
    {
        await foreach (var evt in store.ReadFromAsync(_lastProcessedEventId, cancellationToken))
        {
            Apply(evt);
            _lastProcessedEventId = evt.EventId;
        }
    }

    public void Apply(SourceEvent evt)
    {
        _logger?.LogDebug("Applying event {EventId} ({EventType}) from stream {StreamId}",
            evt.EventId, evt.EventType, evt.StreamId);

        try
        {
            switch (evt.Data)
            {
                case SearchParameterActivated sp:
                    ApplySearchParameterActivated(sp, evt.EventId);
                    break;
                case SearchParameterReindexStarted reindex:
                    ApplyReindexStarted(reindex, evt.EventId);
                    break;
                case SearchParameterReindexCompleted completed:
                    ApplyReindexCompleted(completed, evt.EventId);
                    break;
                case SearchParameterReindexFailed failed:
                    ApplyReindexFailed(failed, evt.EventId);
                    break;
                case SearchParameterTransitionCommitted transition:
                    ApplyTransitionCommitted(transition, evt.EventId);
                    break;
                case SearchParameterDeactivated deactivated:
                    ApplyDeactivated(deactivated, evt.EventId);
                    break;
                case SearchParameterDeleted deleted:
                    ApplyDeleted(deleted);
                    break;
                case StructureDefinitionActivated sd:
                    ApplyStructureDefinitionActivated(sd);
                    break;
                case StructureDefinitionDeactivated sdDeactivated:
                    ApplyStructureDefinitionDeactivated(sdDeactivated);
                    break;
                case PackageActivated pa:
                    ApplyPackageActivated(pa, evt.Timestamp);
                    break;
                case PackageDeactivated pd:
                    ApplyPackageDeactivated(pd, evt.EventId);
                    break;
                default:
                    _logger?.LogWarning("Unknown event type {EventType} at EventId {EventId} - ignoring",
                        evt.Data.GetType().Name, evt.EventId);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to apply event {EventId} ({EventType})",
                evt.EventId, evt.EventType);
            throw new InvalidOperationException(
                $"Event replay failed at EventId {evt.EventId} ({evt.EventType})", ex);
        }
    }

    private void ApplySearchParameterActivated(SearchParameterActivated sp, long eventId)
    {
        var isBaseFhir = IsBaseFhirPackage(sp.SourcePackage.Split('@')[0]);
        var ownershipIssue = ValidateOwnerSearchParamId(sp);
        if (ownershipIssue is not null)
        {
            _logger?.LogWarning(
                "Ignoring invalid {EventType} event {EventId} for {ResourceType}.{Code}: {Message}",
                nameof(SearchParameterActivated),
                eventId,
                sp.ResourceType,
                sp.Code,
                ownershipIssue.Message);
            return;
        }

        var storageIssue = ValidateStorageCanonical(sp, out var storageCanonical);
        if (storageIssue is not null)
        {
            throw new InvalidOperationException(storageIssue.Message);
        }

        var key = (sp.ResourceType, sp.Code);
        _searchParameters.TryGetValue(key, out var existing);
        if (isBaseFhir &&
            existing is { Status: SearchParameterStatus.Enabled } &&
            existing.Canonical == sp.Canonical &&
            IsBaseFhirPackage(existing.SourcePackage.Split('@')[0]))
        {
            return;
        }

        var latest = GetLatestActivation(sp.ResourceType, sp.Code);

        ActiveSearchParameter? outgoing = null;
        if (existing?.Status is SearchParameterStatus.Enabled
            or SearchParameterStatus.Pending
            or SearchParameterStatus.Reindexing
            or SearchParameterStatus.Disabling)
        {
            outgoing = existing;
            outgoing.Status = SearchParameterStatus.Disabling;
            outgoing.DeactivationEventId = eventId;
        }

        if (latest?.Status == SearchParameterStatus.Staged)
        {
            latest.Status = SearchParameterStatus.Disabled;
        }

        var activated = new ActiveSearchParameter
        {
            SearchParamId = sp.SearchParamId,
            Canonical = sp.Canonical,
            Code = sp.Code,
            ResourceType = sp.ResourceType,
            Expression = sp.Expression,
            ParamType = sp.ParamType,
            SourcePackage = sp.SourcePackage,
            OverridesCanonical = storageCanonical == sp.Canonical ? null : storageCanonical,
            TargetResourceTypes = sp.TargetResourceTypes,
            Components = sp.Components,
            Name = sp.Name,
            Description = sp.Description,
            ActivationEventId = eventId,
            PreviousActivationEventId = outgoing is not null ? latest?.ActivationEventId : null,
            IsAvailable = true,
            Status = outgoing is not null
                ? SearchParameterStatus.Staged
                : isBaseFhir
                    ? SearchParameterStatus.Enabled
                    : SearchParameterStatus.Pending
        };
        _searchParameterActivations.Add(activated);

        if (outgoing is null)
        {
            _searchParameters[key] = activated;
        }

        _canonicalToParamId[sp.Canonical] = sp.SearchParamId;
        _storageCanonicals[sp.Canonical] = storageCanonical;

        if (sp.SearchParamId >= _nextSearchParamId)
        {
            _nextSearchParamId = sp.SearchParamId + 1;
        }

    }

    private ValidationIssue? ValidateSearchParameterActivation(
        SearchParameterActivated sp,
        out string storageCanonical)
    {
        var issue = ValidateStorageCanonical(sp, out storageCanonical);
        if (issue is not null)
        {
            return issue;
        }

        return ValidateOwnerSearchParamId(sp);
    }

    private ValidationIssue? ValidateOwnerSearchParamId(SearchParameterActivated sp)
    {
        if (_searchParameters.TryGetValue((sp.ResourceType, sp.Code), out var existing) &&
            existing.Status is not SearchParameterStatus.Disabled &&
            existing.SearchParamId != sp.SearchParamId)
        {
            return new ValidationIssue(
                "SP_STORAGE_IDENTITY",
                $"Search parameter {sp.Canonical} cannot replace {existing.Canonical} with a different parameter ID.",
                sp.ResourceType,
                sp.Code);
        }

        return null;
    }

    private ValidationIssue? ValidateStorageCanonical(SearchParameterActivated sp, out string storageCanonical)
    {
        storageCanonical = string.Empty;
        ValidationIssue Invalid(string message) => new("SP_STORAGE_IDENTITY", message, sp.ResourceType, sp.Code);

        _storageCanonicals.TryGetValue(sp.Canonical, out var previousRoot);
        if (sp.Overrides is null)
        {
            storageCanonical = previousRoot ?? sp.Canonical;
            return null;
        }

        if (sp.Overrides.InheritedParamId != sp.SearchParamId)
        {
            return Invalid($"Search parameter {sp.Canonical} does not retain its inherited parameter ID.");
        }

        var target = sp.Overrides.OverridesCanonical;
        if (_canonicalToParamId.TryGetValue(target, out var targetId) && targetId != sp.Overrides.InheritedParamId)
        {
            return Invalid($"Search parameter {sp.Canonical} has an inherited ID inconsistent with {target}.");
        }

        _storageCanonicals.TryGetValue(target, out var targetRoot);
        if (previousRoot is not null && targetRoot is not null && previousRoot != targetRoot)
        {
            return Invalid($"Search parameter {sp.Canonical} overrides a different storage identity.");
        }

        // Legacy same-canonical upgrade events point P at P. The earlier canonical ancestry is the
        // authority; following that self-edge or choosing P's physical SQL row would split the index.
        if (previousRoot is not null || targetRoot is not null)
        {
            storageCanonical = previousRoot ?? targetRoot!;
            return null;
        }

        if (target == sp.Canonical)
        {
            return Invalid($"Search parameter {sp.Canonical} has a self override with no inherited storage identity.");
        }

        storageCanonical = target;
        return null;
    }

    private void ApplyReindexStarted(SearchParameterReindexStarted reindex, long eventId)
    {
        if (_searchParameters.TryGetValue((reindex.ResourceType, reindex.Code), out var sp))
        {
            if (!CanStartReindex(sp, reindex.ActivationEventId))
            {
                LogIgnoredLifecycleEvent(eventId, nameof(SearchParameterReindexStarted), sp, reindex.ActivationEventId);
                return;
            }

            sp.Status = SearchParameterStatus.Reindexing;
            sp.ReindexJobId = reindex.JobId;
        }
    }

    private void ApplyReindexCompleted(SearchParameterReindexCompleted completed, long eventId)
    {
        if (_searchParameters.TryGetValue((completed.ResourceType, completed.Code), out var sp))
        {
            if (!CanFinishReindex(sp, completed.ActivationEventId, completed.JobId))
            {
                LogIgnoredLifecycleEvent(
                    eventId,
                    nameof(SearchParameterReindexCompleted),
                    sp,
                    completed.ActivationEventId,
                    completed.JobId);
                return;
            }

            sp.Status = SearchParameterStatus.Enabled;
            sp.ReindexJobId = null;
        }
    }

    private void ApplyReindexFailed(SearchParameterReindexFailed failed, long eventId)
    {
        if (_searchParameters.TryGetValue((failed.ResourceType, failed.Code), out var sp))
        {
            if (!CanFinishReindex(sp, failed.ActivationEventId, failed.JobId))
            {
                LogIgnoredLifecycleEvent(
                    eventId,
                    nameof(SearchParameterReindexFailed),
                    sp,
                    failed.ActivationEventId,
                    failed.JobId);
                return;
            }

            sp.Status = SearchParameterStatus.Pending;
            sp.ReindexJobId = null;
        }
    }

    private void ApplyTransitionCommitted(SearchParameterTransitionCommitted transition, long eventId)
    {
        var activationEventIds = transition.ActivationEventIds.ToHashSet();
        var deactivationEventIds = transition.DeactivationEventIds.ToHashSet();
        var staged = _searchParameterActivations.Where(
            parameter => parameter.SearchParamId == transition.SearchParamId &&
                parameter.Status == SearchParameterStatus.Staged &&
                IsLatestActivation(parameter) &&
                activationEventIds.Contains(parameter.ActivationEventId))
            .ToList();
        var outgoing = _searchParameters.Values.Where(
            parameter => parameter.SearchParamId == transition.SearchParamId &&
                parameter.Status == SearchParameterStatus.Disabling &&
                parameter.DeactivationEventId is { } deactivationEventId &&
                deactivationEventIds.Contains(deactivationEventId))
            .ToList();

        var valid = activationEventIds.All(
                activationEventId => staged.Any(parameter => parameter.ActivationEventId == activationEventId)) &&
            deactivationEventIds.All(
                deactivationEventId => outgoing.Any(parameter => parameter.DeactivationEventId == deactivationEventId));

        foreach (var parameter in staged)
        {
            valid &= _searchParameters.TryGetValue((parameter.ResourceType, parameter.Code), out var owner) &&
                owner.SearchParamId == transition.SearchParamId &&
                owner.Status == SearchParameterStatus.Disabling &&
                owner.DeactivationEventId is { } deactivationEventId &&
                deactivationEventIds.Contains(deactivationEventId);
        }

        foreach (var parameter in outgoing)
        {
            var replacement = GetLatestActivation(parameter.ResourceType, parameter.Code);
            valid &= replacement?.Status != SearchParameterStatus.Staged ||
                replacement.SearchParamId == transition.SearchParamId &&
                activationEventIds.Contains(replacement.ActivationEventId);
        }

        if (!valid || (staged.Count == 0 && outgoing.Count == 0))
        {
            _logger?.LogWarning(
                "Ignoring stale {EventType} event {EventId} for SearchParamId {SearchParamId}",
                nameof(SearchParameterTransitionCommitted),
                eventId,
                transition.SearchParamId);
            return;
        }

        foreach (var parameter in outgoing)
        {
            parameter.Status = SearchParameterStatus.Disabled;
            parameter.ReindexJobId = null;
        }

        foreach (var parameter in staged)
        {
            parameter.Status = SearchParameterStatus.Pending;
            _searchParameters[(parameter.ResourceType, parameter.Code)] = parameter;
        }
    }

    private void ApplyDeactivated(SearchParameterDeactivated deactivated, long eventId)
    {
        var parameters = _searchParameterActivations.Where(
            candidate => candidate.ResourceType == deactivated.ResourceType &&
                candidate.Code == deactivated.Code &&
                candidate.Canonical == deactivated.Canonical)
            .ToList();
        foreach (var candidate in parameters)
        {
            candidate.IsAvailable = false;
        }

        var parameter = parameters.LastOrDefault(
            candidate => candidate.Status is not SearchParameterStatus.Disabled);
        if (parameter is not null)
        {
            BeginDeactivation(parameter, eventId);
        }
    }

    private void ApplyDeleted(SearchParameterDeleted deleted)
    {
        if (_searchParameters.TryGetValue((deleted.ResourceType, deleted.Code), out var current) &&
            current.Canonical == deleted.Canonical)
        {
            _searchParameters.Remove((deleted.ResourceType, deleted.Code));
        }
        _searchParameterActivations.RemoveAll(sp => sp.Canonical == deleted.Canonical);
    }

    private void ApplyStructureDefinitionActivated(StructureDefinitionActivated sd)
    {
        _structureDefinitions[sd.Canonical] = new ActiveStructureDefinition
        {
            Canonical = sd.Canonical,
            Type = sd.Type,
            Kind = sd.Kind,
            SourcePackage = sd.SourcePackage,
            SnapshotJson = sd.SnapshotJson
        };
    }

    private void ApplyStructureDefinitionDeactivated(StructureDefinitionDeactivated sdDeactivated)
    {
        _structureDefinitions.Remove(sdDeactivated.Canonical);
    }

    private void ApplyPackageActivated(PackageActivated pa, DateTimeOffset timestamp)
    {
        _packages[$"{pa.PackageId}@{pa.Version}"] = new ActivePackage
        {
            PackageId = pa.PackageId,
            Version = pa.Version,
            ResourceCount = pa.Resources.Count,
            ActivatedAt = timestamp
        };
    }

    private void ApplyPackageDeactivated(PackageDeactivated pd, long eventId)
    {
        _packages.Remove($"{pd.PackageId}@{pd.Version}");
        DeactivateResourcesFromPackage(pd.PackageId, pd.Version, eventId);
    }

    private void DeactivateResourcesFromPackage(string packageId, string version, long eventId)
    {
        var packageKey = $"{packageId}@{version}";

        var packageParameters = _searchParameterActivations
            .Where(sp => sp.SourcePackage == packageKey)
            .ToList();
        foreach (var parameter in packageParameters)
        {
            parameter.IsAvailable = false;
        }

        var parameters = packageParameters
            .Where(sp => sp.Status is not SearchParameterStatus.Disabled)
            .GroupBy(sp => (sp.ResourceType, sp.Code))
            .Select(group => group.Last())
            .ToList();
        foreach (var sp in parameters)
        {
            BeginDeactivation(sp, eventId);
        }

        var sdsToRemove = _structureDefinitions
            .Where(kv => kv.Value.SourcePackage == packageKey)
            .Select(kv => kv.Key)
            .ToList();

        foreach (var key in sdsToRemove)
        {
            _structureDefinitions.Remove(key);
        }
    }

    private void BeginDeactivation(ActiveSearchParameter parameter, long eventId)
    {
        var key = (parameter.ResourceType, parameter.Code);
        if (!_searchParameters.TryGetValue(key, out var outgoing))
        {
            return;
        }

        if (!ReferenceEquals(parameter, outgoing))
        {
            parameter.Status = SearchParameterStatus.Disabled;
        }

        if (outgoing.Status != SearchParameterStatus.Disabling)
        {
            outgoing.Status = SearchParameterStatus.Disabling;
            outgoing.DeactivationEventId = eventId;
            outgoing.ReindexJobId = null;
        }

        var previous = FindAvailablePredecessor(parameter);
        if (previous is null)
        {
            return;
        }

        outgoing.DeactivationEventId = eventId;
        _searchParameterActivations.Add(previous.CloneForRestoration(eventId));
    }

    private ActiveSearchParameter? FindAvailablePredecessor(ActiveSearchParameter parameter)
    {
        var previousActivationEventId = parameter.PreviousActivationEventId;
        while (previousActivationEventId is { } activationEventId)
        {
            var previous = _searchParameterActivations.LastOrDefault(
                candidate =>
                    candidate.ResourceType == parameter.ResourceType &&
                    candidate.Code == parameter.Code &&
                    candidate.ActivationEventId == activationEventId);
            if (previous is null)
            {
                return null;
            }

            if (previous.IsAvailable)
            {
                return previous;
            }

            previousActivationEventId = previous.PreviousActivationEventId;
        }

        return null;
    }

    private ActiveSearchParameter? GetLatestActivation(string resourceType, string code) =>
        _searchParameterActivations.LastOrDefault(
            candidate => candidate.ResourceType == resourceType && candidate.Code == code);

    private bool IsLatestActivation(ActiveSearchParameter parameter) =>
        ReferenceEquals(parameter, GetLatestActivation(parameter.ResourceType, parameter.Code));

    private bool CanStartReindex(ActiveSearchParameter owner, long? activationEventId) =>
        owner.Status == SearchParameterStatus.Pending &&
            IsLatestActivation(owner) &&
            (activationEventId is null || owner.ActivationEventId == activationEventId);

    private bool CanFinishReindex(
        ActiveSearchParameter parameter,
        long? activationEventId,
        string jobId) =>
        parameter.Status == SearchParameterStatus.Reindexing &&
        IsLatestActivation(parameter) &&
        parameter.ReindexJobId == jobId &&
        (activationEventId is null || parameter.ActivationEventId == activationEventId);



    private void LogIgnoredLifecycleEvent(
        long eventId,
        string eventType,
        ActiveSearchParameter parameter,
        long? activationEventId,
        string? jobId = null)
    {
        _logger?.LogWarning(
            "Ignoring stale {EventType} event {EventId} for {ResourceType}.{Code}: " +
            "event activation {EventActivationEventId}, current activation {CurrentActivationEventId}, " +
            "event job {EventJobId}, current job {CurrentJobId}",
            eventType,
            eventId,
            parameter.ResourceType,
            parameter.Code,
            activationEventId,
            parameter.ActivationEventId,
            jobId,
            parameter.ReindexJobId);
    }

    private static bool IsBaseFhirPackage(string packageId) =>
        packageId.StartsWith("hl7.fhir.r", StringComparison.OrdinalIgnoreCase) &&
        packageId.EndsWith(".core", StringComparison.OrdinalIgnoreCase);

    public void Dispose()
    {
        _activationLock?.Dispose();
        GC.SuppressFinalize(this);
    }
}
