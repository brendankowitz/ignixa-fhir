// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.RegularExpressions;
using Ignixa.CliOnFhir.Catalog;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Patients;
using Ignixa.CliOnFhir.Session;
using Ignixa.CliOnFhir.Terminology;

namespace Ignixa.CliOnFhir.CodeSystems;

/// <summary>
/// <c>folder_code_systems</c> / <c>cli-on-fhir systems</c>: which coding systems the folder uses (SNOMED CT, LOINC, ...),
/// how much, where, and their most frequent codes, without terminology or network. Data-quality hints are information
/// issues, never errors. Terminology already loaded in the session adds per-system resolution; it is never loaded here
/// unless asked.
/// </summary>
public static partial class FolderCodeSystems
{
    public const int DefaultTop = 5;
    public const int MaxTop = 50;
    public const int MaxElements = 10;
    public const int MaxListed = 20;

    public const string Basis = "Every Coding in the catalogued resources' JSON (an object with a string code and a string system, or any object "
        + "with a string code in a 'coding' array), including extensions and contained resources, and every Quantity with a unit system; one coding per "
        + "occurrence. Values of FHIR code elements with a FHIR-defined binding (Patient.gender, Observation.status, ...) carry no system in the data and "
        + "are listed separately under implicitCodes, by element, not attributed to a system. No search index is used.";

    private const string PatternCheck = "pattern only (http://hl7.org/fhir/CodeSystem/<name> or http://hl7.org/fhir/ValueSet/<name> used as a system)";
    private const string StoreCheck = "loaded terminology (the session's CodeSystems), then the pattern";

    private static readonly string[] TxSystems = ["http://snomed.info/sct", "http://loinc.org", "http://www.nlm.nih.gov/research/umls/rxnorm"];

    public static ToolEnvelope<CodeSystemsFacts?> List(FolderSession session, int? count, int offset, int? top, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (EnvelopeAssembler.FolderProblem(session) is { } folderProblem)
        {
            return EnvelopeAssembler.Failure<CodeSystemsFacts>(session, folderProblem, cancellationToken);
        }

        try
        {
            return Answer(session, count, offset, top, cancellationToken);
        }
        catch (ToolRequestException ex)
        {
            return EnvelopeAssembler.Failure<CodeSystemsFacts>(session, ex.Issue, cancellationToken);
        }
    }

    /// <summary>
    /// <see cref="List"/> after loading the terminology store when <paramref name="withTerminology"/> is set, with the store's load
    /// problems (as warnings) after the answer's own issues.
    /// </summary>
    public static async Task<ToolEnvelope<CodeSystemsFacts?>> ListAsync(FolderSession session, int? count, int offset, int? top, bool withTerminology, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (!withTerminology || EnvelopeAssembler.FolderProblem(session) is not null)
        {
            return List(session, count, offset, top, cancellationToken);
        }

        TerminologyState state = await session.Terminology.GetAsync(cancellationToken);
        ToolEnvelope<CodeSystemsFacts?> envelope = List(session, count, offset, top, cancellationToken);
        IEnumerable<IssueItem> load = state.IsUnusable ? state.FailureIssues().Select(issue => issue with { Severity = issue.Severity == "error" ? "warning" : issue.Severity }) : state.LoadIssues;
        return envelope with { Issues = [.. envelope.Issues, .. load] };
    }

    /// <summary>The ten most used systems, for <c>folder_inspect</c>.</summary>
    public static IReadOnlyList<CodeSystemBrief> Summary(FolderSession session, int limit = 10)
    {
        ArgumentNullException.ThrowIfNull(session);

        return [.. CodeSystemInventories.For(session.Catalog, session.Schema.Version).Systems
            .Take(limit)
            .Select(tally => new CodeSystemBrief(SafeSystem(tally.System), WellKnownSystems.Identify(tally.System).Name, tally.Codes.Count, tally.Codings))];
    }

    private static ToolEnvelope<CodeSystemsFacts?> Answer(FolderSession session, int? count, int offset, int? top, CancellationToken cancellationToken)
    {
        List<IssueItem> issues = [];
        int pageSize = Paging.Resolve(count, offset, issues);
        int topCodes = Top(top, issues);
        CodeSystemInventory inventory = CodeSystemInventories.For(session.Catalog, session.Schema.Version);
        TerminologyState? terminology = session.Terminology.Current is { IsUnusable: false } loaded ? loaded : null;

        List<OmittedItem> omitted = [];
        List<SystemTally> page = [.. inventory.Systems.Skip(offset).Take(pageSize)];
        int digested = 0;
        List<CodeSystemUsage> systems = [];
        foreach (SystemTally tally in page)
        {
            digested += SafeSystem(tally.System) == tally.System ? 0 : 1;
            systems.Add(Usage(tally, topCodes, terminology, session.Terminology.Options.TxServer is not null, omitted));
        }

        issues.AddRange(Hints(systems, inventory, digested));
        issues.AddRange(Paging.BeyondTotal(offset, inventory.Systems.Count, "code systems"));
        issues.AddRange(inventory.Problems);
        List<NextCall> next = Next(systems, terminology, session, offset, pageSize, inventory.Systems.Count, topCodes);
        if (TxHint(systems, session) is { } tx)
        {
            issues.Add(tx);
        }

        var facts = new CodeSystemsFacts(
            Basis,
            inventory.ResourcesRead,
            inventory.ElapsedMilliseconds,
            inventory.Systems.Count,
            offset,
            systems.Count,
            topCodes,
            systems,
            [.. inventory.Implicit.Take(MaxListed).Select(tally => new ImplicitCodeUsage(tally.Element, tally.Codes.Count, tally.Occurrences, [.. tally.Codes.Take(topCodes).Select(code => new CodeCount(TerminologyNames.Code(code.Code), code.Count))]))],
            [.. inventory.WithoutSystem.Take(MaxListed)],
            terminology is null ? "not loaded" : "loaded",
            terminology is null ? PatternCheck : StoreCheck);
        if (inventory.Implicit.Count > MaxListed)
        {
            omitted.Add(new OmittedItem($"{inventory.Implicit.Count - MaxListed} more implicit code elements", "limit"));
        }

        if (inventory.WithoutSystem.Count > MaxListed)
        {
            omitted.Add(new OmittedItem($"{inventory.WithoutSystem.Count - MaxListed} more elements with codings without a system", "limit"));
        }

        int remaining = inventory.Systems.Count - offset - systems.Count;
        if (remaining > 0)
        {
            omitted.Add(new OmittedItem($"{remaining} more code systems", "limit"));
        }

        return EnvelopeAssembler.Create<CodeSystemsFacts?>(session, facts, session.Catalog.BuiltAt, [], omitted, [], issues, cancellationToken) with { Next = next };
    }

    private static CodeSystemUsage Usage(SystemTally tally, int top, TerminologyState? terminology, bool txServer, List<OmittedItem> omitted)
    {
        string system = SafeSystem(tally.System);
        SystemIdentity identity = WellKnownSystems.Identify(tally.System);
        List<string> flags = [];
        if (identity.PreferredUri is not null)
        {
            flags.Add("non-preferred-uri");
        }

        string? suggested = null;
        if (FhirLookalike().Match(tally.System) is { Success: true } lookalike)
        {
            suggested = $"http://hl7.org/fhir/{lookalike.Groups["name"].Value}";
            CodeSystemResolution? actual = terminology?.Store.ResolveCodeSystem(tally.System, version: null);
            bool known = actual is not null && (actual.Entry is not null || actual.Stubs.Count > 0 || actual.LoadedVersions.Count > 0);
            bool suggestionKnown = terminology?.Store.ResolveCodeSystem(suggested, version: null) is { Entry: not null };
            if (known)
            {
                suggested = null;
            }
            else
            {
                flags.Add(suggestionKnown ? "not-a-fhir-canonical" : "possibly-not-a-fhir-canonical");
            }
        }

        if (tally.Elements.Count > MaxElements)
        {
            omitted.Add(new OmittedItem($"{tally.Elements.Count - MaxElements} more elements of {system}", "limit"));
        }

        return new CodeSystemUsage(
            system,
            identity.Name,
            identity.Steward,
            identity.Kind,
            identity.PreferredUri,
            suggested,
            flags,
            tally.Codes.Count,
            tally.Codings,
            tally.Resources,
            [.. tally.Elements.Take(MaxElements)],
            [.. tally.Codes.Take(top).Select(code => new CodeCount($"{system}|{TerminologyNames.Code(code.Code)}", code.Count))],
            terminology is null ? null : Status(terminology.Store, tally, txServer));
    }

    private static SystemTerminologyStatus Status(TerminologyStore store, SystemTally tally, bool txServer)
    {
        if (store.ResolveCodeSystem(tally.System, version: null).Entry is not { } entry)
        {
            return new SystemTerminologyStatus(txServer ? "tx-server-available" : "not-loaded", null, null, null, null, null, null);
        }

        int resolved = 0;
        int inactive = 0;
        foreach ((string code, _) in tally.Codes)
        {
            if (entry.Find(code) is { } concept)
            {
                resolved++;
                inactive += concept.Inactive ? 1 : 0;
            }
        }

        return new SystemTerminologyStatus("local", entry.Origin.Source.Name, entry.Content, entry.Version, resolved, inactive, tally.Codes.Count - resolved);
    }

    private static IEnumerable<IssueItem> Hints(List<CodeSystemUsage> systems, CodeSystemInventory inventory, int digested)
    {
        List<CodeSystemUsage> aliases = [.. systems.Where(system => system.PreferredUri is not null)];
        if (aliases.Count > 0)
        {
            yield return new IssueItem("information", "non-preferred-system-uri", $"{aliases.Count} systems on this page are written with a non-preferred identifier of a well-known system: {string.Join("; ", aliases.Take(5).Select(system => $"{system.System} is {system.Name}, preferred {system.PreferredUri}"))}.", Location: null);
        }

        List<CodeSystemUsage> lookalikes = [.. systems.Where(system => system.SuggestedCanonical is not null)];
        if (lookalikes.Count > 0)
        {
            yield return new IssueItem("information", "not-a-fhir-canonical", $"{lookalikes.Count} systems on this page look like FHIR-defined code systems but are not their canonical: {string.Join("; ", lookalikes.Take(5).Select(system => $"{system.System} (probably {system.SuggestedCanonical}; {(system.Flags.Contains("not-a-fhir-canonical") ? "checked against loaded terminology" : "by pattern only, load the core package to check")})"))}.", Location: null);
        }

        if (inventory.WithoutSystem.Count > 0)
        {
            yield return new IssueItem("information", "codings-without-system", $"{inventory.WithoutSystem.Sum(element => element.Count)} codings have a code but no system ({string.Join(", ", inventory.WithoutSystem.Take(5).Select(element => $"{element.Element}: {element.Count}"))}); their meaning cannot be resolved.", Location: null);
        }

        if (digested > 0)
        {
            yield return new IssueItem("information", "invalid-value", $"{digested} system URIs on this page are not http(s) or urn URIs without whitespace or control characters, so they are shown as digests ('~' and 16 hex digits).", Location: null);
        }
    }

    // Built from structure only: tool names, paging, and package ids of the HL7 packages that carry FHIR and HL7 Terminology systems.
    private static List<NextCall> Next(List<CodeSystemUsage> systems, TerminologyState? terminology, FolderSession session, int offset, int pageSize, int total, int top)
    {
        List<NextCall> next = [];
        if (offset + systems.Count < total)
        {
            next.Add(new NextCall("folder_code_systems", new Dictionary<string, object> { ["offset"] = offset + systems.Count, ["count"] = pageSize, ["top"] = top }));
        }

        bool Missing(CodeSystemUsage system) => system.Terminology is null or { Loaded: not "local" };
        HashSet<string> loadedPackages = terminology is null ? [] : [.. terminology.Store.Sources.Select(source => source.Info.PackageId ?? string.Empty)];
        if (systems.Any(system => system.Kind is "hl7-v2" or "hl7-v3" or "hl7-terminology" && Missing(system)))
        {
            string tho = DefaultCorePackages.TerminologyPackageFor(session.Schema.Version);
            if (!loadedPackages.Contains(tho))
            {
                next.Add(new NextCall("terminology_load_package", new Dictionary<string, object> { ["package"] = tho }));
            }
        }

        if (systems.Any(system => system.Kind == "fhir" && Missing(system)) && DefaultCorePackages.For(session.Schema.Version) is { } core && !loadedPackages.Contains(core.Id))
        {
            next.Add(new NextCall("terminology_load_package", new Dictionary<string, object> { ["package"] = core.ToString() }));
        }

        return next;
    }

    private static IssueItem? TxHint(List<CodeSystemUsage> systems, FolderSession session) =>
        session.Terminology.Options.TxServer is null && systems.Any(system => TxSystems.Contains(system.System, StringComparer.Ordinal))
            ? new IssueItem("information", "tx-server-not-enabled", "SNOMED CT, LOINC and RxNorm are not distributed as FHIR packages; to look their codes up, start the server with --tx-server (for example https://tx.fhir.org/r4; opt-in, sends codes only).", Location: null)
            : null;

    private static int Top(int? top, List<IssueItem> issues)
    {
        if (top is < 1)
        {
            throw ToolRequestException.Error("invalid-parameter", $"'top' must be at least 1, not {top}.");
        }

        if (top > MaxTop)
        {
            issues.Add(new IssueItem("warning", "top-clamped", $"top {top} exceeds the maximum of {MaxTop}, so {MaxTop} codes are listed per system.", Location: null));
            return MaxTop;
        }

        return top ?? DefaultTop;
    }

    /// <summary>The system URI as written when it is an http(s) or urn URI without whitespace or control characters; otherwise its digest.</summary>
    public static string SafeSystem(string system) => system.Length <= 256 && SystemShape().IsMatch(system) ? system : FhirIds.Digest(system);

    [GeneratedRegex("^(https?|urn):[^\\s\\p{C}\"'<>\\\\|]+$")]
    private static partial Regex SystemShape();

    [GeneratedRegex("^http://hl7\\.org/fhir/(CodeSystem|ValueSet)/(?<name>[A-Za-z0-9.\\-]+)$")]
    private static partial Regex FhirLookalike();
}
