// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Ignixa.CliOnFhir.Caching;
using Ignixa.CliOnFhir.Catalog;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Reading;
using Ignixa.CliOnFhir.Session;
using Ignixa.CliOnFhir.Terminology.Npm;

namespace Ignixa.CliOnFhir.Terminology;

/// <summary>
/// A folder session's terminology. Nothing is loaded until the first terminology call, which loads the folder's own
/// CodeSystems, ValueSets and ConceptMaps, then the configured packages (or, with none configured, the FHIR version's core
/// package). <see cref="LoadPackageAsync"/> adds a package later. Each load publishes a new immutable
/// <see cref="TerminologyState"/>; loads are serialized, reads never wait. A package that fails to load is reported with
/// every answer and is not retried by itself; load it again explicitly.
/// </summary>
[SuppressMessage("Design", "CA1001", Justification = "The SemaphoreSlim never allocates a wait handle (AvailableWaitHandle is not used), so it holds nothing to dispose; the session lives as long as its folder session.")]
public sealed class TerminologySession
{
    public const string FolderResourceUnreadableCode = "terminology-resource-unreadable";

    private static readonly string[] TerminologyTypes = ["CodeSystem", "ValueSet", "ConceptMap"];

    private readonly FolderSession _folder;
    private readonly TerminologyOptions _options;
    private readonly PackageAcquisition _acquisition;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile TerminologyState? _state;

    public TerminologySession(FolderSession folder, TerminologyOptions options)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(options);

        _folder = folder;
        _options = options;
        _acquisition = new PackageAcquisition(options.Store, options.FhirPackageCache, options.Registry, new FolderOverlap(folder.RootPath));
    }

    public TerminologyOptions Options => _options;

    /// <summary>The current state; null until a terminology call loaded it.</summary>
    public TerminologyState? Current => _state;

    /// <summary>The current state, loading it on the first call.</summary>
    public Task<TerminologyState> GetAsync(CancellationToken cancellationToken) => GetAsync(firstLoad: null, cancellationToken);

    /// <param name="firstLoad">
    /// The package a load call asks for. When it is the default core package and the session has not loaded yet, the
    /// default load is skipped so that this call loads it, explicitly.
    /// </param>
    internal async Task<TerminologyState> GetAsync(PackageRequest? firstLoad, CancellationToken cancellationToken)
    {
        if (_state is { } state)
        {
            return state;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            return _state ??= await InitialLoadAsync(firstLoad, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Adds <paramref name="request"/> to the store (after every source loaded so far), unless it is already loaded.</summary>
    /// <exception cref="ToolRequestException">The package cannot be found, downloaded or read.</exception>
    public async Task<PackageLoadOutcome> LoadPackageAsync(PackageRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await GetAsync(request, cancellationToken);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            TerminologyState state = _state!;
            if (request.Version is not null && Loaded(state, request.Id, request.Version) is { } existing)
            {
                return new PackageLoadOutcome(existing, null, AlreadyLoaded: true, state);
            }

            AcquiredPackage acquired = await _acquisition.AcquireAsync(request, cancellationToken);
            if (Loaded(state, acquired.Id, acquired.Version) is { } loaded)
            {
                return new PackageLoadOutcome(loaded, acquired, AlreadyLoaded: true, state);
            }

            LoadedSource source = Load(acquired, isDefault: false, cancellationToken);
            TerminologyStore store = state.Store.With(source, _folder.UtcNow());
            (long heap, long workingSet) = Memory();
            _state = state with { Store = store, LoadIssues = [.. state.LoadIssues, .. acquired.Warnings, .. FhirVersionIssues(source), .. source.Problems], HeapBytes = heap, WorkingSetBytes = workingSet };
            return new PackageLoadOutcome(source, acquired, AlreadyLoaded: false, _state);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<TerminologyState> InitialLoadAsync(PackageRequest? firstLoad, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        List<LoadedSource> sources = [];
        List<IssueItem> issues = [];
        if (LoadFolder(issues, cancellationToken) is { } folder)
        {
            sources.Add(folder);
            issues.AddRange(folder.Problems);
        }

        bool usingDefault = _options.Packages.Count == 0;
        PackageRequest? core = DefaultCorePackages.For(_folder.Schema.Version);
        bool loadingCoreExplicitly = usingDefault && core is not null && firstLoad is not null && firstLoad.Id == core.Id && (firstLoad.Version is null || firstLoad.Version == core.Version);
        IReadOnlyList<PackageRequest> requests = !usingDefault
            ? _options.Packages
            : core is not null && !loadingCoreExplicitly ? [core] : [];
        if (usingDefault && core is null)
        {
            issues.Add(new IssueItem("information", "no-default-core-package", $"No core package is loaded by default for FHIR {_folder.Schema.Version}; load one with terminology_load_package.", Location: null));
        }

        foreach (PackageRequest request in requests)
        {
            try
            {
                AcquiredPackage acquired = await _acquisition.AcquireAsync(request, cancellationToken);
                if (sources.Any(source => source.Info.PackageId == acquired.Id && source.Info.PackageVersion == acquired.Version))
                {
                    continue;
                }

                LoadedSource source = Load(acquired, usingDefault, cancellationToken);
                sources.Add(source);
                issues.AddRange(acquired.Warnings);
                issues.AddRange(FhirVersionIssues(source));
                issues.AddRange(source.Problems);
            }
            catch (ToolRequestException ex)
            {
                issues.Add(ex.Issue with
                {
                    Severity = "warning",
                    Diagnostics = $"The terminology package {request} was not loaded: {ex.Issue.Diagnostics}",
                });
            }
        }

        // Canonical conflicts are reported by each answer that uses one, and listed by terminology_list.
        TerminologyStore store = TerminologyStore.Build(sources, _folder.UtcNow());
        (long heap, long workingSet) = Memory();
        return new TerminologyState(store, issues, stopwatch.ElapsedMilliseconds, heap, workingSet);
    }

    private LoadedSource? LoadFolder(List<IssueItem> issues, CancellationToken cancellationToken)
    {
        FolderCatalog catalog = _folder.Catalog;
        List<CatalogEntry> entries = [.. catalog.Entries.Where(entry => TerminologyTypes.Contains(entry.ResourceType, StringComparer.Ordinal))];
        if (entries.Count == 0)
        {
            return null;
        }

        List<TerminologyFile> files = [];
        foreach (CatalogEntry entry in entries)
        {
            (byte[]? json, IssueItem? problem) = ResourceReader.ReadBytes(catalog.RootPath, entry);
            if (json is null)
            {
                issues.Add(problem! with { Severity = "warning", Code = FolderResourceUnreadableCode, Diagnostics = $"A terminology resource in the folder was not loaded: {problem.Diagnostics}" });
                continue;
            }

            files.Add(new TerminologyFile(entry.FilePath, json, entry.Line, entry.Offset));
        }

        return TerminologySourceLoader.Load(TerminologySourceInfo.Folder(catalog.RootPath), [], files, filesIgnored: 0, cancellationToken);
    }

    // A package declaring only other FHIR versions may define the same canonicals differently (R4 versus R5 core).
    private IEnumerable<IssueItem> FhirVersionIssues(LoadedSource source)
    {
        string prefix = _folder.Schema.Version switch
        {
            Abstractions.FhirVersion.Stu3 => "3.0",
            Abstractions.FhirVersion.R4 => "4.0",
            Abstractions.FhirVersion.R4B => "4.3",
            Abstractions.FhirVersion.R5 => "5.0",
            _ => "6.0",
        };
        if (source.FhirVersions.Count > 0 && !source.FhirVersions.Any(version => version.StartsWith(prefix, StringComparison.Ordinal)))
        {
            yield return new IssueItem(
                "warning",
                "package-fhir-version-mismatch",
                $"{source.Info.Name} declares FHIR {string.Join(", ", source.FhirVersions.Select(TerminologyNames.Version))}, not the session's {_folder.Schema.Version}; its terminology may differ from what this FHIR version defines.",
                source.Info.EvidencePrefix + "package/package.json");
        }
    }

    private static LoadedSource Load(AcquiredPackage acquired, bool isDefault, CancellationToken cancellationToken)
    {
        LoadedSource source = TerminologySourceLoader.Load(
            TerminologySourceInfo.Package(acquired.Id, acquired.Version, acquired.Origin, acquired.Location, isDefault),
            acquired.Contents.FhirVersions,
            acquired.Contents.Files,
            acquired.Contents.FilesIgnored,
            cancellationToken);

        // The archive reader already set aside the other resource types; the loader only saw terminology files.
        return source with { Counts = source.Counts with { OtherResources = source.Counts.OtherResources + acquired.Contents.OtherResources } };
    }

    private static LoadedSource? Loaded(TerminologyState state, string id, string version) =>
        state.Store.Sources.FirstOrDefault(source => source.Info.PackageId == id && source.Info.PackageVersion == version);

    private static (long Heap, long WorkingSet) Memory() => (GC.GetTotalMemory(forceFullCollection: true), Environment.WorkingSet);
}
