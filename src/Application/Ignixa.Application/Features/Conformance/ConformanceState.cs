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
        _searchParameters.Values.FirstOrDefault(sp => sp.Canonical == canonical);

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
        foreach (var (key, parameter) in _searchParameters)
        {
            staged._searchParameters.Add(key, new ActiveSearchParameter
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
                Status = parameter.Status,
                ReindexJobId = parameter.ReindexJobId
            });
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
                    ApplySearchParameterActivated(sp, evt.Timestamp);
                    break;
                case SearchParameterReindexStarted reindex:
                    ApplyReindexStarted(reindex);
                    break;
                case SearchParameterReindexCompleted completed:
                    ApplyReindexCompleted(completed);
                    break;
                case SearchParameterReindexFailed failed:
                    ApplyReindexFailed(failed);
                    break;
                case SearchParameterDeactivated deactivated:
                    ApplyDeactivated(deactivated);
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
                    ApplyPackageDeactivated(pd);
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

    private void ApplySearchParameterActivated(SearchParameterActivated sp, DateTimeOffset timestamp)
    {
        var isBaseFhir = IsBaseFhirPackage(sp.SourcePackage.Split('@')[0]);
        var issue = ValidateStorageCanonical(sp, out var storageCanonical);
        if (issue is not null)
        {
            throw new InvalidOperationException(issue.Message);
        }

        _searchParameters[(sp.ResourceType, sp.Code)] = new ActiveSearchParameter
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
            Status = isBaseFhir ? SearchParameterStatus.Enabled : SearchParameterStatus.Pending
        };

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

    private void ApplyReindexStarted(SearchParameterReindexStarted reindex)
    {
        if (_searchParameters.TryGetValue((reindex.ResourceType, reindex.Code), out var sp))
        {
            sp.Status = SearchParameterStatus.Reindexing;
            sp.ReindexJobId = reindex.JobId;
        }
    }

    private void ApplyReindexCompleted(SearchParameterReindexCompleted completed)
    {
        if (_searchParameters.TryGetValue((completed.ResourceType, completed.Code), out var sp))
        {
            sp.Status = SearchParameterStatus.Enabled;
            sp.ReindexJobId = null;
        }
    }

    private void ApplyReindexFailed(SearchParameterReindexFailed failed)
    {
        if (_searchParameters.TryGetValue((failed.ResourceType, failed.Code), out var sp))
        {
            sp.Status = SearchParameterStatus.Pending;
            sp.ReindexJobId = null;
        }
    }

    private void ApplyDeactivated(SearchParameterDeactivated deactivated)
    {
        if (_searchParameters.TryGetValue((deactivated.ResourceType, deactivated.Code), out var sp))
        {
            sp.Status = SearchParameterStatus.Disabled;
        }
    }

    private void ApplyDeleted(SearchParameterDeleted deleted)
    {
        _searchParameters.Remove((deleted.ResourceType, deleted.Code));
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

    private void ApplyPackageDeactivated(PackageDeactivated pd)
    {
        _packages.Remove($"{pd.PackageId}@{pd.Version}");
        DeactivateResourcesFromPackage(pd.PackageId, pd.Version);
    }

    private void DeactivateResourcesFromPackage(string packageId, string version)
    {
        var packageKey = $"{packageId}@{version}";

        foreach (var sp in _searchParameters.Values.Where(sp => sp.SourcePackage == packageKey))
        {
            sp.Status = SearchParameterStatus.Disabled;
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

    private static bool IsBaseFhirPackage(string packageId) =>
        packageId.StartsWith("hl7.fhir.r", StringComparison.OrdinalIgnoreCase) &&
        packageId.EndsWith(".core", StringComparison.OrdinalIgnoreCase);

    public void Dispose()
    {
        _activationLock?.Dispose();
        GC.SuppressFinalize(this);
    }
}
