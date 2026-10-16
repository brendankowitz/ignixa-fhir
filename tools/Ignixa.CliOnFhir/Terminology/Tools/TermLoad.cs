// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Session;
using Ignixa.CliOnFhir.Terminology.Npm;

namespace Ignixa.CliOnFhir.Terminology.Tools;

/// <summary><c>terminology_load_package</c> / <c>cli-on-fhir term load</c>: adds a FHIR npm package to the session's terminology.</summary>
public static class TermLoad
{
    public const int ListedCodeSystems = 50;

    public static Task<ToolEnvelope<TermLoadFacts?>> LoadAsync(FolderSession session, string? package, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        // Timed from the start: on the session's first terminology call this includes loading the folder and default sources.
        var stopwatch = Stopwatch.StartNew();
        PackageRequest? request = null;
        return TerminologyCall.RunAsync<TermLoadFacts>(
            session,
            async _ =>
            {
                PackageLoadOutcome outcome = await session.Terminology.LoadPackageAsync(request!, cancellationToken);
                return Answer(request!, outcome, stopwatch.ElapsedMilliseconds);
            },
            cancellationToken,
            evenWhenUnusable: true,
            loadState: token =>
            {
                // Parsed first, so the session's first load knows when this call is itself loading the default core package.
                request = PackageRequest.Parse(TermArguments.Required(package, "package"));
                return session.Terminology.GetAsync(request, token);
            });
    }

    private static TerminologyAnswer<TermLoadFacts> Answer(PackageRequest request, PackageLoadOutcome outcome, long elapsed)
    {
        LoadedSource source = outcome.Source;
        List<TermResourceSummary> codeSystems = [.. source.CodeSystems.Select(TermResourceSummary.Of).OrderBy(item => item.Url, StringComparer.Ordinal).ThenBy(item => item.Version, StringComparer.Ordinal)];
        List<OmittedItem> omitted = [];
        List<NextCall> next = [];
        if (codeSystems.Count > ListedCodeSystems)
        {
            omitted.Add(new OmittedItem($"{codeSystems.Count - ListedCodeSystems} more CodeSystems of {source.Info.Name}", "limit"));
            next.Add(TermNext.Call("list", new Dictionary<string, object> { ["kind"] = "codeSystems", ["package"] = source.Info.Name, ["offset"] = 0, ["count"] = 50 }));
        }

        var facts = new TermLoadFacts(
            source.Info.Name,
            request.ToString(),
            request.Version is null ? "registry-latest" : "requested",
            source.Info.Origin,
            outcome.AlreadyLoaded,
            TermSourceSummary.Of(source),
            elapsed,
            [.. codeSystems.Take(ListedCodeSystems)]);

        List<IssueItem> issues = outcome.AlreadyLoaded
            ? [new IssueItem("information", "package-already-loaded", $"{source.Info.Name} was already loaded in this session; nothing was read again.", Location: null)]
            : [];
        var evidence = new EvidenceItem(source.Info.Name, source.Info.EvidencePrefix + "package/package.json", null, 0);
        return new TerminologyAnswer<TermLoadFacts>(facts, [evidence], omitted, [], issues) { Next = next, LoadIssues = outcome.State.LoadIssues };
    }
}
