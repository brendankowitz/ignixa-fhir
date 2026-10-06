using System.Collections.Concurrent;
using Ignixa.Conformance.Events;
using Ignixa.Conformance.Events.Abstractions;
using Ignixa.Conformance.Events.Events;
using Ignixa.Conformance.Events.Models;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.Features.Conformance;

public sealed class ConformanceState : IDisposable
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

    public async Task<IDisposable> AcquireActivationLockAsync(CancellationToken cancellationToken)
    {
        await _activationLock.WaitAsync(cancellationToken);
        return new LockReleaser(_activationLock);
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

    private sealed class LockReleaser(SemaphoreSlim semaphore) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (!_disposed)
            {
                semaphore.Release();
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

    public ActiveSearchParameter? FindExtractedByCanonical(string canonical) =>
        _searchParameters.Values.LastOrDefault(sp => sp.Canonical == canonical);

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

    internal ValidationIssue? ApplyProposedEvent(NewSourceEvent proposed)
    {
        if (proposed.Data is SearchParameterActivated parameter &&
            ValidateStorageCanonical(parameter, out _) is { } issue)
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
        await _activationLock.WaitAsync(cancellationToken);
        try
        {
            await foreach (var evt in store.ReadAllAsync(cancellationToken))
            {
                Apply(evt);
                _lastProcessedEventId = evt.EventId;
            }
            _isInitialized = true;
        }
        finally
        {
            _activationLock.Release();
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
        await _activationLock.WaitAsync(cancellationToken);
        try
        {
            foreach (var evt in events)
            {
                Apply(evt);
                _lastProcessedEventId = evt.EventId;
            }
        }
        finally
        {
            _activationLock.Release();
        }
    }

    public async Task CatchUpAsync(
        ISourceEventStore store,
        CancellationToken cancellationToken)
    {
        await _activationLock.WaitAsync(cancellationToken);
        try
        {
            await foreach (var evt in store.ReadFromAsync(_lastProcessedEventId, cancellationToken))
            {
                Apply(evt);
                _lastProcessedEventId = evt.EventId;
            }
        }
        finally
        {
            _activationLock.Release();
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
        var issue = ValidateStorageCanonical(sp, out var storageCanonical);
        if (issue is not null)
        {
            throw new InvalidOperationException(issue.Message);
        }

        var key = (sp.ResourceType, sp.Code);
        _searchParameters.TryGetValue(key, out var existing);
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
        var changed = false;

        foreach (var parameter in _searchParameters.Values.Where(
            sp => sp.SearchParamId == transition.SearchParamId &&
                sp.Status == SearchParameterStatus.Disabling &&
                sp.DeactivationEventId is { } deactivationEventId &&
                deactivationEventIds.Contains(deactivationEventId)))
        {
            parameter.Status = SearchParameterStatus.Disabled;
            parameter.ReindexJobId = null;
            changed = true;
        }

        foreach (var parameter in _searchParameterActivations.Where(
            sp => sp.SearchParamId == transition.SearchParamId &&
                sp.Status == SearchParameterStatus.Staged &&
                IsLatestActivation(sp) &&
                activationEventIds.Contains(sp.ActivationEventId)))
        {
            parameter.Status = SearchParameterStatus.Pending;
            _searchParameters[(parameter.ResourceType, parameter.Code)] = parameter;
            changed = true;
        }

        if (!changed)
        {
            _logger?.LogWarning(
                "Ignoring stale {EventType} event {EventId} for SearchParamId {SearchParamId}",
                nameof(SearchParameterTransitionCommitted),
                eventId,
                transition.SearchParamId);
        }
    }

    private void ApplyDeactivated(SearchParameterDeactivated deactivated, long eventId)
    {
        var parameter = _searchParameterActivations.LastOrDefault(
            candidate => candidate.ResourceType == deactivated.ResourceType &&
                candidate.Code == deactivated.Code &&
                candidate.Canonical == deactivated.Canonical &&
                candidate.Status is not SearchParameterStatus.Disabled);
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

        var parameters = _searchParameterActivations
            .Where(sp => sp.SourcePackage == packageKey && sp.Status is not SearchParameterStatus.Disabled)
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

        outgoing.Status = SearchParameterStatus.Disabling;
        outgoing.DeactivationEventId = eventId;
        outgoing.ReindexJobId = null;

        if (parameter.PreviousActivationEventId is not { } previousActivationEventId)
        {
            return;
        }

        var previous = _searchParameterActivations.LastOrDefault(
            candidate => candidate.ActivationEventId == previousActivationEventId);
        if (previous is null)
        {
            return;
        }

        _searchParameterActivations.Add(CloneForRestoration(previous, eventId));
    }

    private ActiveSearchParameter? GetLatestActivation(string resourceType, string code) =>
        _searchParameterActivations.LastOrDefault(
            candidate => candidate.ResourceType == resourceType && candidate.Code == code);

    private bool IsLatestActivation(ActiveSearchParameter parameter) =>
        ReferenceEquals(parameter, GetLatestActivation(parameter.ResourceType, parameter.Code));

    private bool CanStartReindex(ActiveSearchParameter owner, long? activationEventId)
    {
        if (activationEventId is null)
        {
            return true;
        }

        return owner.Status == SearchParameterStatus.Pending &&
            IsLatestActivation(owner) &&
            owner.ActivationEventId == activationEventId;
    }

    private bool CanFinishReindex(
        ActiveSearchParameter parameter,
        long? activationEventId,
        string jobId) =>
        activationEventId is null ||
        parameter.Status == SearchParameterStatus.Reindexing &&
        IsLatestActivation(parameter) &&
        parameter.ActivationEventId == activationEventId &&
        parameter.ReindexJobId == jobId;

    private static ActiveSearchParameter CloneForRestoration(ActiveSearchParameter previous, long eventId) =>
        new()
        {
            SearchParamId = previous.SearchParamId,
            Canonical = previous.Canonical,
            Code = previous.Code,
            ResourceType = previous.ResourceType,
            Expression = previous.Expression,
            ParamType = previous.ParamType,
            SourcePackage = previous.SourcePackage,
            OverridesCanonical = previous.OverridesCanonical,
            TargetResourceTypes = previous.TargetResourceTypes,
            Components = previous.Components,
            Name = previous.Name,
            Description = previous.Description,
            ActivationEventId = eventId,
            PreviousActivationEventId = previous.PreviousActivationEventId,
            Status = SearchParameterStatus.Staged,
        };

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
