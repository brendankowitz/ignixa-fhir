// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Ignixa.CliOnFhir.Catalog;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Output;
using Ignixa.CliOnFhir.Reading;
using Ignixa.CliOnFhir.Session;
using Ignixa.CliOnFhir.Validation;
using Ignixa.FhirMappingLanguage.Evaluation;
using Ignixa.FhirMappingLanguage.Expressions;
using Ignixa.FhirPath.Evaluation;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Microsoft.Extensions.Logging;

namespace Ignixa.CliOnFhir.Mapping;

/// <summary>
/// Runs a FHIR Mapping Language map over resources of the folder with <c>Ignixa.FhirMappingLanguage</c> (CLI <c>map</c>; the
/// design's <c>fml_transform</c>). The map is FML or a StructureMap, given inline, in a file, or in the folder, with its imports
/// resolved from the same places (<see cref="MapImports"/>); nothing is fetched. Each resource in scope of the map's source type is
/// read from the catalog by its recorded location, its Bundle and conditional references read as the <c>Type/id</c> the index
/// resolved them to, and run through the map's first group on its own, within <see cref="MappingEvaluatorOptions.TransformTimeout"/>.
/// The result is a preview of the first transformed resources, or every one written to <c>&lt;Type&gt;.ndjson</c> in a directory
/// outside the folder, all or none (<see cref="NdjsonDirectory"/>).
/// </summary>
public static partial class MapRunner
{
    /// <summary>How long a run may take unless the call says; the FML transform's own default.</summary>
    public const int DefaultTimeoutSeconds = 30;

    /// <summary>The longest timeout a call may ask for.</summary>
    public const int MaxTimeoutSeconds = 3600;

    /// <summary>The most resources a run reads unless the call says; like the cohort tool's limit.</summary>
    public const int DefaultMaxResources = 50_000;

    /// <summary>The most transformed resources a preview returns.</summary>
    public const int PreviewCount = 5;

    private const string TimeoutCode = "TRANSFORM_TIMEOUT";

    private const string BudgetAlternatives = "out to write every transformed resource to files, or a narrower scope (references, a query)";

    /// <summary>
    /// Runs the map <paramref name="request"/> names. A request that cannot be answered (a map that does not parse, an unresolved
    /// import, an output directory in the folder, a run that takes too long) is an envelope with null facts and an <c>error</c> issue,
    /// and writes nothing. A failure of the tool itself is the same, as <c>map-internal-error</c>, logged by type and stack only.
    /// </summary>
    /// <param name="logger">Receives what is known of a <c>map-internal-error</c>; required, so that no caller can drop it.</param>
    public static Task<ToolEnvelope<MapFacts?>> RunAsync(FolderSession session, MapRequest request, ILogger logger, CancellationToken cancellationToken) =>
        RunAsync(session, request, logger, MappingEvaluatorOptions.Default.TransformTimeout, afterBegin: null, timeoutSignal: CancellationToken.None, cancellationToken);

    /// <param name="transformTimeout">The longest one resource's transformation may take; a test passes a short one.</param>
    /// <param name="afterBegin">Runs after the output file is begun, before anything is written to it; a test swaps its directory there.</param>
    /// <param name="timeoutSignal">Stops the run as its timeout does; a test passes one already cancelled.</param>
    internal static async Task<ToolEnvelope<MapFacts?>> RunAsync(
        FolderSession session, MapRequest request, ILogger logger, TimeSpan transformTimeout, Action<OutputFile>? afterBegin, CancellationToken timeoutSignal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(logger);

        if (EnvelopeAssembler.FolderProblem(session) is { } folderProblem)
        {
            return EnvelopeAssembler.Failure<MapFacts>(session, folderProblem, cancellationToken);
        }

        List<IssueItem> notes = [];
        var run = new Run(session, request, notes, transformTimeout, afterBegin);
        try
        {
            return await WithinTimeAsync(run, timeoutSignal, cancellationToken);
        }
        catch (ToolRequestException ex)
        {
            return Failure(session, ex.Issue, notes, cancellationToken);
        }
#pragma warning disable CA1031 // The boundary of the verb: anything unexpected is logged and reported as an error, never as a success.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            // Not the exception itself: its message can quote the map or a resource.
            logger.LogError("The map run failed unexpectedly with {ExceptionType} (message digest {Digest}) at {StackTrace}", ex.GetType().FullName, FhirIds.Digest(ex.Message), ex.StackTrace);
            return Failure(
                session,
                new IssueItem("error", "map-internal-error", $"The map run failed unexpectedly with {ex.GetType().Name} (digest {FhirIds.Digest(ex.Message)}); this is a defect of the tool or of Ignixa.FhirMappingLanguage, not of the request; its type and stack are in the log on stderr. Nothing was written.", Location: null),
                notes,
                cancellationToken);
        }
    }

    // The request and what it needs on every step.
    private sealed record Run(FolderSession Session, MapRequest Request, List<IssueItem> Notes, TimeSpan TransformTimeout, Action<OutputFile>? AfterBegin)
    {
        public bool ResolvingScope { get; set; }
    }

    // What the run produced, for the response.
    private sealed record Outcome(
        Run Run, LoadedMap Loaded, MapExpression Map, MapEntry Entry, MapImports.Resolved Resolved, ValidationScope Scope, MapTally Tally, string? Out, NdjsonFileFacts? File, int Budget, List<IssueItem> Issues);

    // A failure still reports what it left behind (a temporary file it could not safely delete).
    private static ToolEnvelope<MapFacts?> Failure(FolderSession session, IssueItem error, List<IssueItem> notes, CancellationToken cancellationToken) =>
        EnvelopeAssembler.FolderProblem(session) is not null || notes.Count == 0
            ? EnvelopeAssembler.Failure<MapFacts>(session, error, cancellationToken)
            : EnvelopeAssembler.Create<MapFacts?>(session, null, session.Catalog.BuiltAt, [], [], [], [error, .. notes], cancellationToken);

    private static async Task<ToolEnvelope<MapFacts?>> WithinTimeAsync(Run run, CancellationToken timeoutSignal, CancellationToken cancellationToken)
    {
        int seconds = run.Request.TimeoutSeconds ?? DefaultTimeoutSeconds;
        if (seconds is < 1 or > MaxTimeoutSeconds)
        {
            throw ToolRequestException.Error("invalid-parameter", $"'timeout' must be 1 to {MaxTimeoutSeconds} seconds, not {seconds}.");
        }

        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSignal);
        timeout.CancelAfter(TimeSpan.FromSeconds(seconds));
        try
        {
            return await ExecuteAsync(run, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
        {
            string cold = run.ResolvingScope
                ? " The time ran out while the scope was being worked out, which a first run over a folder does from the search indexes it needs; a build cut by the timeout keeps nothing, so run it once with a higher timeout (for example --timeout 300) to build them and keep them in the index cache."
                : string.Empty;
            throw ToolRequestException.Error("map-timeout", $"The map run did not finish within {seconds} seconds and was stopped; nothing was written.{cold} Narrow the scope (patient, references, a query) or raise the timeout (up to {MaxTimeoutSeconds}).");
        }
    }

    private static async Task<ToolEnvelope<MapFacts?>> ExecuteAsync(Run run, CancellationToken cancellationToken)
    {
        FolderSession session = run.Session;
        MapRequest request = run.Request;
        List<IssueItem> issues = [];
        int budget = ResponseBudget.Resolve(request.MaxBytes, session.MaxResponseBytes, issues, BudgetAlternatives);
        if (request.Out is null && request.Overwrite)
        {
            throw ToolRequestException.Error("invalid-parameter", "'overwrite' applies to 'out', which was not given.");
        }

        var folderMaps = new Lazy<IReadOnlyList<FolderStructureMap>>(() => MapLocator.FolderMaps(session));
        LoadedMap loaded = MapLocator.Resolve(session, request.Map, folderMaps);
        MapExpression map = MapCompiler.Compile(loaded, session.Schema.Version);
        MapEntry entry = MapEntry.Of(map, session.Schema);
        MapImports.Resolved resolved = MapImports.Resolve(session, loaded, map, request.Imports, folderMaps);
        OutputDirectory? directory = request.Out is null ? null : OutputDirectory.Resolve(session.RootPath, request.Out, request.Overwrite);

        ValidationScope scope = await ScopeAsync(run, entry, cancellationToken);
        CheckResourceLimit(request, scope);

        NdjsonDirectory? output = directory is null ? null : new NdjsonDirectory(directory, run.Notes) { AfterBegin = run.AfterBegin };
        output?.Check(entry.TargetType);
        var transform = new MapTransform(session.Schema, resolved.Map, entry, run.TransformTimeout);
        var tally = new MapTally(output is null ? PreviewCount : 0);
        NdjsonFileFacts? file = await TransformAllAsync(run, transform, entry, scope.Entries, output, tally, cancellationToken);

        issues.AddRange(scope.Issues);
        issues.AddRange(resolved.Notes);
        issues.AddRange(tally.SkippedByType.Select(skipped => new IssueItem("warning", "map-source-mismatch", $"{skipped.Value} {skipped.Key} resources in scope are not the map's source type, {entry.SourceType}, so they were skipped.", Location: null)));
        issues.AddRange(tally.FailureIssues);
        if (tally.Empty > 0)
        {
            issues.Add(new IssueItem("warning", "map-output-empty", $"{tally.Empty} of the {tally.Transformed} transformed resources hold nothing but their resourceType: none of the map's rules wrote anything for them (their sources matched nothing, or a condition held them back).", Location: null));
        }

        issues.AddRange(run.Notes);
        if (output is not null && file is null)
        {
            issues.Add(new IssueItem("information", "out-not-created", $"No resource was transformed, so no file was created in '{output.Path}'.", Location: null));
        }

        var outcome = new Outcome(run, loaded, map, entry, resolved, scope, tally, output?.Path, file, budget, issues);
        return output is null ? Preview(outcome, cancellationToken) : Written(outcome, cancellationToken);
    }

    // The scope as validate chooses it; with no scope argument, the folder's resources of the map's source type.
    private static async Task<ValidationScope> ScopeAsync(Run run, MapEntry entry, CancellationToken cancellationToken)
    {
        MapRequest request = run.Request;
        bool unscoped = request.ResourceType is null && request.Query is null && request.Patient is null && request.References is not { Count: > 0 };
        string? type = unscoped ? entry.SourceType : request.ResourceType;
        run.ResolvingScope = true;
        ValidationScope scope = await Task.Run(() => ValidationScope.Resolve(run.Session, type, request.Query, request.Patient, request.References), CancellationToken.None).WaitAsync(cancellationToken);
        run.ResolvingScope = false;
        return scope;
    }

    private static void CheckResourceLimit(MapRequest request, ValidationScope scope)
    {
        int limit = request.MaxResources ?? DefaultMaxResources;
        if (limit < 1)
        {
            throw ToolRequestException.Error("invalid-parameter", $"'maxResources' must be 1 or more, not {limit}.");
        }

        if (scope.Entries.Count > limit)
        {
            throw ToolRequestException.Error("too-many-resources", $"The scope holds {scope.Entries.Count} resources, more than the limit of {limit}; nothing was read. Narrow it (patient, references, a query) or raise maxResources.");
        }
    }

    // Every resource in scope order; with an output directory, the file is committed once every resource is done, or not at all.
    private static async Task<NdjsonFileFacts?> TransformAllAsync(
        Run run, MapTransform transform, MapEntry entry, IReadOnlyList<CatalogEntry> entries, NdjsonDirectory? output, MapTally tally, CancellationToken cancellationToken)
    {
        try
        {
            foreach (CatalogEntry resource in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entry.SourceType is not null && resource.ResourceType != entry.SourceType)
                {
                    tally.Skip(resource.ResourceType);
                    continue;
                }

                if (TransformOne(run, transform, resource, tally, cancellationToken) is { } result && output is not null)
                {
                    await output.WriteAsync(entry.TargetType, result.ToJsonString(), cancellationToken);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            return output is null ? null : (await output.CommitAsync()).SingleOrDefault();
        }
        catch (Exception ex) when (output is not null && ex is IOException or UnauthorizedAccessException)
        {
            await output.DiscardAsync();
            throw ToolRequestException.Error("out-write-failed", $"Writing into '{output.Path}' failed ({ex.GetType().Name}: {ex.Message}); nothing was written unless a note says otherwise.");
        }
        catch when (output is not null)
        {
            await output.DiscardAsync();
            throw;
        }
    }

    // The transformed resource, or null when the map failed on it (counted, and an issue).
    private static JsonObject? TransformOne(Run run, MapTransform transform, CatalogEntry entry, MapTally tally, CancellationToken cancellationToken)
    {
        if (entry.Length > transform.Options.MaxInputResourceSizeBytes)
        {
            tally.Fail(entry, NotTransformed(entry, "map-resource-too-large", $"is {entry.Length} bytes, more than the {transform.Options.MaxInputResourceSizeBytes} a map is run on"));
            return null;
        }

        (byte[]? json, IssueItem? problem) = ResourceReader.ReadBytes(run.Session.RootPath, entry);
        if (json is null)
        {
            tally.Fail(entry, problem!);
            return null;
        }

        ResourceJsonNode source;
        try
        {
            source = JsonSourceNodeFactory.Parse(json);
        }
        catch (JsonException)
        {
            tally.Fail(entry, NotTransformed(entry, "invalid-resource", "is not valid JSON where the catalog recorded it"));
            return null;
        }

        _ = run.Session.BundleReferences.Rewrite(entry, ((IMutableJsonNode)source).MutableNode);
        JsonObject result;
        try
        {
            result = transform.Apply(source, cancellationToken);
        }
        catch (MappingExecutionException ex) when (ex.Code == TimeoutCode)
        {
            tally.Fail(entry, NotTransformed(entry, "map-transform-timeout", $"was not transformed: the map did not finish within {transform.Options.TransformTimeout.TotalSeconds} seconds (TransformTimeout) on it"));
            return null;
        }
        catch (Exception ex) when (IsMapFailure(ex))
        {
            string code = ex is MappingExecutionException { Code: { } known } && LibraryCode().IsMatch(known) ? $", {known}" : string.Empty;
            tally.Fail(entry, NotTransformed(entry, "map-transform-failed", $"was not transformed: the map failed on it ({ex.GetType().Name}{code}; the message is not repeated because it can quote the resource or the map, digest {FhirIds.Digest(ex.Message)})"));
            return null;
        }

        tally.Transform(entry, result);
        return result;
    }

    private static bool IsMapFailure(Exception ex) =>
        ex is MappingExecutionException or InvalidOperationException or ArgumentException or NotSupportedException or FormatException
            or InvalidCastException or FhirPathEvaluationException or KeyNotFoundException or JsonException or OverflowException;

    private static IssueItem NotTransformed(CatalogEntry entry, string code, string what) =>
        new("error", code, $"{ResourceReader.Describe(entry)} {what}.", entry.Line is int line ? $"{entry.FilePath}:{line}" : entry.FilePath);

    private static ToolEnvelope<MapFacts?> Written(Outcome outcome, CancellationToken cancellationToken)
    {
        List<OmittedItem> omitted = Omitted(outcome);
        if (outcome.Tally.Transformed > 0)
        {
            omitted.Add(new OmittedItem($"evidence: the {outcome.Tally.Transformed} resources transformed are not cited one by one (only those the map failed on are); the file holds them", "limit"));
        }

        MapFacts facts = Facts(outcome) with { Out = outcome.Out, File = outcome.File };
        return EnvelopeAssembler.Create<MapFacts?>(outcome.Run.Session, facts, outcome.Scope.BuiltAt, Evidence(outcome, []), omitted, outcome.Scope.Absent, outcome.Issues, cancellationToken);
    }

    // As many of the kept resources as fit the budget; adding one never makes the envelope smaller.
    private static ToolEnvelope<MapFacts?> Preview(Outcome outcome, CancellationToken cancellationToken)
    {
        int fits = 0;
        int tooMany = outcome.Tally.Kept.Count + 1;
        while (tooMany - fits > 1)
        {
            int middle = fits + ((tooMany - fits) / 2);
            (MapFacts facts, List<EvidenceItem> evidence, List<OmittedItem> omitted) = PreviewParts(outcome, middle);
            if (EnvelopeAssembler.MaxSize(outcome.Run.Session, facts, evidence, omitted, outcome.Scope.Absent, outcome.Issues, []) <= outcome.Budget)
            {
                fits = middle;
            }
            else
            {
                tooMany = middle;
            }
        }

        (MapFacts shown, List<EvidenceItem> cited, List<OmittedItem> left) = PreviewParts(outcome, fits);
        return EnvelopeAssembler.Create<MapFacts?>(outcome.Run.Session, shown, outcome.Scope.BuiltAt, cited, left, outcome.Scope.Absent, outcome.Issues, cancellationToken);
    }

    private static (MapFacts Facts, List<EvidenceItem> Evidence, List<OmittedItem> Omitted) PreviewParts(Outcome outcome, int count)
    {
        List<(CatalogEntry Source, JsonObject Result)> shown = [.. outcome.Tally.Kept.Take(count)];
        List<OmittedItem> omitted = Omitted(outcome);
        int remaining = outcome.Tally.Transformed - count;
        if (remaining > 0)
        {
            string why = count < outcome.Tally.Kept.Count ? "size-budget" : "limit";
            omitted.Add(new OmittedItem($"{remaining} more transformed resources (a preview returns at most {PreviewCount}); out writes every one to a file", why));
        }

        MapFacts facts = Facts(outcome) with
        {
            Returned = shown.Count,
            Resources = [.. shown.Select(item => (JsonNode)item.Result)],
            Sources = [.. shown.Select(item => ResourceReader.Describe(item.Source))],
        };
        return (facts, Evidence(outcome, [.. shown.Select(item => item.Source)]), omitted);
    }

    private static MapFacts Facts(Outcome outcome)
    {
        MapEntry entry = outcome.Entry;
        LoadedMap loaded = outcome.Loaded;
        var source = new MapSourceFacts(
            loaded.Source,
            loaded.Format,
            FhirIds.Written(outcome.Map.Url),
            Identifier().IsMatch(entry.Group) ? entry.Group : FhirIds.Digest(entry.Group),
            entry.SourceType,
            entry.TargetType,
            loaded.Source == LoadedMap.FileSource ? loaded.File : null,
            loaded.Reference);
        int processed = outcome.Scope.Entries.Count;
        return new MapFacts(source, outcome.Resolved.Imports, outcome.Scope.Facts, processed, outcome.Tally.Transformed, outcome.Tally.Failed, outcome.Tally.Skipped);
    }

    // The map's own place when it is in the folder, the sources of the resources shown, then the resources the map failed on.
    private static List<EvidenceItem> Evidence(Outcome outcome, IReadOnlyList<CatalogEntry> shown)
    {
        List<EvidenceItem> evidence = outcome.Loaded.Evidence is { } map ? [map] : [];
        evidence.AddRange(shown.Select(entry => new EvidenceItem(ResourceReader.Describe(entry), entry.FilePath, entry.Line, entry.Offset)));
        evidence.AddRange(outcome.Tally.Cited);
        return evidence;
    }

    private static List<OmittedItem> Omitted(Outcome outcome)
    {
        List<OmittedItem> omitted = [.. outcome.Scope.Omitted];
        if (outcome.Tally.Uncited > 0)
        {
            omitted.Add(new OmittedItem($"{outcome.Tally.Uncited} more resources the map failed on have no issue of their own (the first {MapTally.MaxFailureIssues} do)", "limit"));
        }

        omitted.Add(new OmittedItem("next: no call is listed, because the fml_transform tool does not exist yet and the map verb is CLI only; run the same map with out set to a directory outside the folder for every transformed resource", "limit"));
        return omitted;
    }

    // Ignixa.FhirMappingLanguage's error codes are constants such as MAX_RECURSION_EXCEEDED.
    [GeneratedRegex("^[A-Z][A-Z_]{0,39}$", RegexOptions.CultureInvariant)]
    private static partial Regex LibraryCode();

    // A group name the response repeats as it is: an identifier, as FML spells one.
    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex Identifier();
}
