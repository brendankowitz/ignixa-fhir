// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using Ignixa.CliOnFhir.Catalog;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Indexing;
using Ignixa.CliOnFhir.Output;
using Ignixa.CliOnFhir.Reading;
using Ignixa.CliOnFhir.Session;
using Ignixa.CliOnFhir.Validation;
using Ignixa.DeId;
using Ignixa.DeId.Models;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Microsoft.Extensions.Logging;

namespace Ignixa.CliOnFhir.DeId;

/// <summary>
/// De-identifies resources of the folder with <c>Ignixa.DeId</c> into <c>&lt;Type&gt;.ndjson</c> files in a directory outside it
/// (CLI <c>deid</c>; the design's <c>deid_resources</c>). The scope is chosen as <c>validate</c> chooses it (folder, type, search,
/// patient compartment, references) and each resource is read from the catalog by its recorded location; Bundle and conditional
/// references are written as the <c>Type/id</c> the index resolved them to, since the Bundle they were relative to is not written. A
/// resource is written only when every rule applied cleanly: one the library could not de-identify, or de-identified with a warning
/// (a rule whose FHIRPath failed, a processor that failed and was skipped), is not written and is an issue, since it may still hold
/// what a rule was to remove. The files are written all or none (<see cref="NdjsonDirectory"/>), and nothing of the configuration
/// or of a resource is ever repeated in the response or the log.
/// </summary>
public static class DeIdRunner
{
    /// <summary>How long a run may take unless the call says; like the FML transform's default.</summary>
    public const int DefaultTimeoutSeconds = 30;

    /// <summary>The longest timeout a call may ask for.</summary>
    public const int MaxTimeoutSeconds = 3600;

    /// <summary>The most resources a run reads unless the call says; like the cohort tool's limit.</summary>
    public const int DefaultMaxResources = 50_000;

    private static readonly RequestOptions Settings = new();

    /// <summary>
    /// Runs the de-identification <paramref name="request"/> describes. A request that cannot be answered (no or an invalid
    /// configuration, an output directory in the folder, an unknown scope, a run that takes too long) is an envelope with null facts
    /// and an <c>error</c> issue, and leaves no file behind. A failure of the tool itself is the same, as <c>deid-internal-error</c>,
    /// and is logged by type and stack only (its message could quote the configuration or a resource).
    /// </summary>
    /// <param name="logger">Receives what is known of a <c>deid-internal-error</c>; required, so that no caller can drop it.</param>
    public static Task<ToolEnvelope<DeIdFacts?>> RunAsync(FolderSession session, DeIdRequest request, ILogger logger, CancellationToken cancellationToken) =>
        RunAsync(session, request, logger, afterBegin: null, timeoutSignal: CancellationToken.None, cancellationToken);

    /// <param name="afterBegin">Runs after each output file is begun, before anything is written to it; a test swaps its directory or fails the write there.</param>
    /// <param name="timeoutSignal">Stops the run as its timeout does; a test passes one already cancelled.</param>
    internal static async Task<ToolEnvelope<DeIdFacts?>> RunAsync(
        FolderSession session, DeIdRequest request, ILogger logger, Action<OutputFile>? afterBegin, CancellationToken timeoutSignal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(logger);

        if (EnvelopeAssembler.FolderProblem(session) is { } folderProblem)
        {
            return EnvelopeAssembler.Failure<DeIdFacts>(session, folderProblem, cancellationToken);
        }

        List<IssueItem> notes = [];
        try
        {
            return await WithinTimeAsync(session, request, notes, afterBegin, timeoutSignal, cancellationToken);
        }
        catch (ToolRequestException ex)
        {
            return Failure(session, ex.Issue, notes, cancellationToken);
        }
#pragma warning disable CA1031 // The boundary of the verb: anything unexpected is logged and reported as an error, never as a success.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            // Not the exception itself: its message (and its inner exceptions') can quote a key or a resource.
            logger.LogError("The de-identification run failed unexpectedly with {ExceptionType} (message digest {Digest}) at {StackTrace}", ex.GetType().FullName, FhirIds.Digest(ex.Message), ex.StackTrace);
            return Failure(
                session,
                new IssueItem("error", "deid-internal-error", $"The de-identification run failed unexpectedly with {ex.GetType().Name} (digest {FhirIds.Digest(ex.Message)}); this is a defect of the tool, not of the request; its type and stack are in the log on stderr. Nothing was written.", Location: null),
                notes,
                cancellationToken);
        }
    }

    // Which step a run is in, for a timeout to say what used the time.
    private sealed class RunProgress
    {
        public bool ResolvingScope { get; set; }
    }

    // A failure still reports what it left behind (a temporary file it could not safely delete).
    private static ToolEnvelope<DeIdFacts?> Failure(FolderSession session, IssueItem error, List<IssueItem> notes, CancellationToken cancellationToken) =>
        EnvelopeAssembler.FolderProblem(session) is not null || notes.Count == 0
            ? EnvelopeAssembler.Failure<DeIdFacts>(session, error, cancellationToken)
            : EnvelopeAssembler.Create<DeIdFacts?>(session, null, session.Catalog.BuiltAt, [], [], [], [error, .. notes], cancellationToken);

    private static async Task<ToolEnvelope<DeIdFacts?>> WithinTimeAsync(
        FolderSession session, DeIdRequest request, List<IssueItem> notes, Action<OutputFile>? afterBegin, CancellationToken timeoutSignal, CancellationToken cancellationToken)
    {
        int seconds = request.TimeoutSeconds ?? DefaultTimeoutSeconds;
        if (seconds is < 1 or > MaxTimeoutSeconds)
        {
            throw ToolRequestException.Error("invalid-parameter", $"'timeout' must be 1 to {MaxTimeoutSeconds} seconds, not {seconds}.");
        }

        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSignal);
        timeout.CancelAfter(TimeSpan.FromSeconds(seconds));
        var progress = new RunProgress();
        try
        {
            return await ExecuteAsync(session, request, notes, progress, afterBegin, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
        {
            string narrower = request.Patient is null && request.References is not { Count: > 0 } ? "Limit it to one patient's resources with patient, or raise" : "Raise";
            string cold = progress.ResolvingScope
                ? " The time ran out while the scope was being worked out, which a first run over a folder does from the search indexes it needs; a build cut by the timeout keeps nothing, so run it once with a higher timeout (for example --timeout 300) to build them and keep them in the index cache."
                : string.Empty;
            throw ToolRequestException.Error("deid-timeout", $"The de-identification did not finish within {seconds} seconds and was stopped; nothing was written.{cold} {narrower} the timeout (up to {MaxTimeoutSeconds}).");
        }
    }

    private static async Task<ToolEnvelope<DeIdFacts?>> ExecuteAsync(
        FolderSession session, DeIdRequest request, List<IssueItem> notes, RunProgress progress, Action<OutputFile>? afterBegin, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Out))
        {
            throw ToolRequestException.Error("invalid-parameter", "'out' is required: deid writes one NDJSON file per resource type into that directory, which must be outside the folder; it returns no resource.");
        }

        DeIdConfiguration config = DeIdConfiguration.Load(request.Config, session.Schema);
        OutputDirectory directory = OutputDirectory.Resolve(session.RootPath, request.Out, request.Overwrite);

        progress.ResolvingScope = true;
        ValidationScope scope = await Task.Run(() => ValidationScope.Resolve(session, request.ResourceType, request.Query, request.Patient, request.References), CancellationToken.None).WaitAsync(cancellationToken);
        progress.ResolvingScope = false;
        CheckResourceLimit(request, scope);

        List<string> types = [.. scope.Entries.Select(entry => entry.ResourceType).Distinct(StringComparer.Ordinal)];
        var output = new NdjsonDirectory(directory, notes) { AfterBegin = afterBegin };
        foreach (string type in types)
        {
            output.Check(type);
        }

        using DeIdEngineHost host = DeIdEngineHost.Create(config.Options, session.Schema);
        var tally = new DeIdTally();
        IReadOnlyList<NdjsonFileFacts> files = await WriteAsync(session, host.Engine, scope.Entries, output, tally, cancellationToken);

        DeIdFacts facts = Facts(scope, config, output, types, tally, files);
        List<IssueItem> issues = [.. scope.Issues, .. config.Notes, .. DuplicateIdIssues.For(session.Catalog, types), .. tally.FailureIssues, .. notes];
        if (files.Count == 0)
        {
            issues.Add(new IssueItem("information", "out-not-created", $"No resource was written, so no file was created in '{directory.Path}'.", Location: null));
        }

        return EnvelopeAssembler.Create<DeIdFacts?>(session, facts, scope.BuiltAt, tally.Cited, Omitted(scope, tally), scope.Absent, issues, cancellationToken);
    }

    private static void CheckResourceLimit(DeIdRequest request, ValidationScope scope)
    {
        int limit = request.MaxResources ?? DefaultMaxResources;
        if (limit < 1)
        {
            throw ToolRequestException.Error("invalid-parameter", $"'maxResources' must be 1 or more, not {limit}.");
        }

        if (scope.Entries.Count > limit)
        {
            string narrower = request.Patient is null && request.References is not { Count: > 0 } ? "Limit it to one patient's resources with patient, a type, or a search, or raise" : "Raise";
            throw ToolRequestException.Error("too-many-resources", $"The scope holds {scope.Entries.Count} resources, more than the limit of {limit}; nothing was read. {narrower} maxResources.");
        }
    }

    // Every resource in scope order, then every file committed together; anything that stops the run first leaves nothing new.
    private static async Task<IReadOnlyList<NdjsonFileFacts>> WriteAsync(
        FolderSession session, IDeIdEngine engine, IReadOnlyList<CatalogEntry> entries, NdjsonDirectory output, DeIdTally tally, CancellationToken cancellationToken)
    {
        try
        {
            foreach (CatalogEntry entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await DeIdentifyAsync(session, engine, entry, output, tally, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            return await output.CommitAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await output.DiscardAsync();
            throw ToolRequestException.Error("out-write-failed", $"Writing into '{output.Path}' failed ({ex.GetType().Name}: {ex.Message}); nothing was written unless a note says otherwise.");
        }
        catch
        {
            await output.DiscardAsync();
            throw;
        }
    }

    private static async Task DeIdentifyAsync(FolderSession session, IDeIdEngine engine, CatalogEntry entry, NdjsonDirectory output, DeIdTally tally, CancellationToken cancellationToken)
    {
        (byte[]? json, IssueItem? problem) = ResourceReader.ReadBytes(session.RootPath, entry);
        if (json is null)
        {
            tally.Fail(entry, problem!);
            return;
        }

        ResourceJsonNode resource;
        try
        {
            resource = JsonSourceNodeFactory.Parse(json);
        }
        catch (JsonException)
        {
            tally.Fail(entry, NotWritten(entry, "invalid-resource", "is not valid JSON where the catalog recorded it"));
            return;
        }

        (ReferenceResolution resolution, _) = session.BundleReferences.Rewrite(entry, ((IMutableJsonNode)resource).MutableNode);
        Result<DeIdResult> result = await engine.DeidentifyAsync(resource, Settings, cancellationToken);
        if (!result.IsSuccess)
        {
            tally.Fail(entry, NotWritten(entry, "deid-resource-failed", $"could not be de-identified (Ignixa.DeId {Code(result.Error.Code)})"));
            return;
        }

        if (result.Value.Warnings.Length > 0)
        {
            tally.Fail(entry, NotWritten(entry, "deid-resource-incomplete", $"was de-identified with {result.Value.Warnings.Length} warning(s) (a rule whose FHIRPath failed on it, or a method that failed on one of its elements), so it may still hold what a rule was to remove"));
            return;
        }

        await output.WriteAsync(entry.ResourceType, result.Value.DeidentifiedJson, cancellationToken);
        tally.Wrote(entry, result.Value.Metrics.OperationCounts, resolution.Resolved.Count);
    }

    // The library's messages quote rule paths and the resource, so only its error code is given.
    private static IssueItem NotWritten(CatalogEntry entry, string code, string what) =>
        new("error", code, $"{ResourceReader.Describe(entry)} {what}; it was not written. The library's message is not repeated because it can quote the resource or the configuration.", entry.Line is int line ? $"{entry.FilePath}:{line}" : entry.FilePath);

    private static string Code(string code) =>
        code.Length <= 40 && code.All(c => c is (>= 'A' and <= 'Z') or '_') ? code : FhirIds.Digest(code);

    private static DeIdFacts Facts(ValidationScope scope, DeIdConfiguration config, NdjsonDirectory output, List<string> types, DeIdTally tally, IReadOnlyList<NdjsonFileFacts> files)
    {
        Dictionary<string, NdjsonFileFacts> byType = files.ToDictionary(file => file.ResourceType, StringComparer.Ordinal);
        List<DeIdTypeFacts> perType = [.. types.Select(type =>
        {
            (int written, int failed) = tally.Of(type);
            NdjsonFileFacts? file = byType.GetValueOrDefault(type);
            return new DeIdTypeFacts(type, scope.Entries.Count(entry => entry.ResourceType == type), written, failed, file?.Path, file?.Bytes);
        })];

        return new DeIdFacts(scope.Facts, config.Facts, output.Path, perType, tally.Operations, scope.Entries.Count, tally.Written, tally.Failed, tally.ReferencesResolved);
    }

    private static List<OmittedItem> Omitted(ValidationScope scope, DeIdTally tally)
    {
        List<OmittedItem> omitted = [.. scope.Omitted];
        if (tally.Uncited > 0)
        {
            omitted.Add(new OmittedItem($"{tally.Uncited} more resources that were not written have no issue of their own (the first {DeIdTally.MaxFailureIssues} do); facts.types counts them per type", "limit"));
        }

        if (tally.Written > 0)
        {
            omitted.Add(new OmittedItem($"evidence: the {tally.Written} resources written are not cited one by one (only those not written are); facts.types counts them per type, and the files hold them", "limit"));
        }

        omitted.Add(new OmittedItem("next: no call is listed, because the deid_resources tool does not exist yet and the deid verb is CLI only; open the output directory with the inspect verb to explore what was written", "limit"));
        return omitted;
    }
}
