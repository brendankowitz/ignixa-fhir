// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.CliOnFhir.Catalog;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Session;
using Ignixa.CliOnFhir.Terminology;
using Ignixa.CliOnFhir.Terminology.Npm;
using Ignixa.Serialization;
using Ignixa.Validation;
using Ignixa.Validation.Abstractions;

namespace Ignixa.CliOnFhir.Validation;

/// <summary>
/// <c>validate_resources</c> / <c>cli-on-fhir validate</c>: validates resources in the folder with <c>Ignixa.Validation</c>
/// against their core definition and loaded profiles, binding checks answered by the session's terminology store, and
/// summarizes the result for an agent: verdict counts, checks that did not conclude, and the issues grouped by severity,
/// profile, rule, path and message template with sample resources. A resource is <c>valid</c> only when every check ran;
/// one whose bindings could not be checked or whose profile is not loaded is <c>unknown</c>. Nothing from the data reaches
/// the summary, the issue messages or <c>next</c> except shape-checked ids, paths and canonicals; full OperationOutcomes,
/// which quote the data, come only with <c>detail</c>.
/// </summary>
public static class ResourceValidator
{
    public const int DefaultLimit = 200;

    public const int MaxLimit = 2000;

    public const int DefaultDetailLimit = 10;

    public const int MaxDetailLimit = 50;

    /// <summary>Groups listed per response; the rest are counted in <c>omitted</c>.</summary>
    public const int MaxGroups = 50;

    private const int ListedProfiles = 5;

    /// <summary>Unreadable-resource issues listed per response; the rest are counted in <c>omitted</c>.</summary>
    private const int MaxListedProblems = 20;

    public static async Task<ToolEnvelope<ValidateFacts?>> ValidateAsync(FolderSession session, ValidateRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);

        if (EnvelopeAssembler.FolderProblem(session) is { } folderProblem)
        {
            return EnvelopeAssembler.Failure<ValidateFacts>(session, folderProblem, cancellationToken);
        }

        try
        {
            return await ExecuteAsync(session, request, cancellationToken);
        }
        catch (ToolRequestException ex)
        {
            return EnvelopeAssembler.Failure<ValidateFacts>(session, ex.Issue, cancellationToken);
        }
    }

    private static async Task<ToolEnvelope<ValidateFacts?>> ExecuteAsync(FolderSession session, ValidateRequest request, CancellationToken cancellationToken)
    {
        ValidationDepth depth = Depth(request.Depth);
        int limit = Limit(request);
        List<PackageRequest> packageRequests = [.. (request.Packages ?? []).Select(PackageRequest.Parse).Distinct()];

        // The terminology store first (the folder's, then the configured or core packages), then each requested package,
        // read once for both its profiles and its terminology.
        TerminologyState terminology = await session.Terminology.GetAsync(cancellationToken);
        List<AcquiredPackage> packages = [];
        foreach (PackageRequest packageRequest in packageRequests)
        {
            AcquiredPackage acquired = await session.Terminology.Acquisition.AcquireAsync(packageRequest, cancellationToken, keepStructureDefinitions: true);
            terminology = (await session.Terminology.LoadAcquiredAsync(acquired, cancellationToken)).State;
            packages.Add(acquired);
        }

        ProfileSet profiles = session.Validators.For(packages);
        LoadedProfile? requested = request.Profile is { } canonical ? RequestedProfile(profiles, canonical, request.ResourceType) : null;
        bool byReference = request.References is { Count: > 0 };
        ValidationScope scope = ValidationScope.Resolve(session, byReference ? request : request with { ResourceType = request.ResourceType ?? requested?.Type });
        List<CatalogEntry> inScope = [.. scope.Entries];
        List<OmittedItem> omitted = [];
        if (requested is not null && byReference)
        {
            int others = inScope.RemoveAll(entry => entry.ResourceType != requested.Type);
            if (others > 0)
            {
                omitted.Add(new OmittedItem($"{others} referenced resources that are not {requested.Type}, which the requested profile does not apply to", "not-applicable"));
            }
        }

        if (request.Offset < 0)
        {
            throw ToolRequestException.Error("invalid-parameter", $"'offset' must not be negative, not {request.Offset}.");
        }

        List<CatalogEntry> page = [.. inScope.Skip(request.Offset).Take(limit)];
        List<IssueItem> requestIssues = [.. PagingNotes(request, limit, page.Count, inScope.Count)];
        var check = new ResourceCheck(session.RootPath, profiles, new ValidationSettings { Depth = depth, TerminologyService = session.Validators.Terminology }, requested);
        List<ResourceValidation> validations = [.. page.Select(entry =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return check.Validate(entry);
        })];

        var summary = new IssueSummary();
        validations.ForEach(summary.Add);

        List<IssueItem> issues = [.. requestIssues, .. scope.Issues, .. profiles.Problems];
        List<IssueItem> unreadable = [.. validations.Where(validation => validation.Problem is not null).Select(validation => validation.Problem!)];
        issues.AddRange(unreadable.Take(MaxListedProblems));
        if (unreadable.Count > MaxListedProblems)
        {
            omitted.Add(new OmittedItem($"{unreadable.Count - MaxListedProblems} more unreadable-resource issues (facts.verdicts.unreadable counts them all)", "limit"));
        }

        issues.AddRange(NotCheckedIssues(summary, session, terminology));
        issues.AddRange(ProfileNotLoadedIssue(validations));
        issues.AddRange(terminology.LoadIssues);

        int next = request.Offset + page.Count;
        if (next < inScope.Count)
        {
            omitted.Add(new OmittedItem($"{inScope.Count - next} more resources in scope (offset={next})", "limit"));
        }

        IReadOnlyList<(IssueGroup Group, IReadOnlyList<CatalogEntry> Samples)> groups = summary.Groups();
        var against = new ValidationContextFacts(
            depth == ValidationDepth.Full ? "full" : "spec",
            requested is null ? null : TerminologyNames.Canonical(requested.Url, requested.Version),
            profiles.Packages,
            profiles.Profiles.Count,
            [.. terminology.Store.Sources.Select(source => source.Info.Name)],
            session.Terminology.Options.TxServer is not null,
            DisplaysChecked: false,
            ChecksNotRun(depth));
        var facts = new ValidateFacts(scope.Facts with { Total = inScope.Count }, against, request.Offset, validations.Count, summary.Verdicts, summary.NotChecked, [], groups.Count);
        List<NextCall> nextCalls = Next(request, next < inScope.Count ? next : null, groups);
        return Fit(session, scope, facts, groups, request.Detail ? validations : null, omitted, issues, nextCalls, cancellationToken);
    }

    /// <summary>
    /// The envelope with as many groups (up to <see cref="MaxGroups"/>), and with <c>detail</c> as many outcomes, as fit under
    /// the size cap, in order; then, if it still does not fit, as many issues and <c>absent</c> entries as do. What is left out
    /// is counted in <c>omitted</c>. Evidence is the sample resources of the listed groups.
    /// </summary>
    private static ToolEnvelope<ValidateFacts?> Fit(
        FolderSession session,
        ValidationScope scope,
        ValidateFacts facts,
        IReadOnlyList<(IssueGroup Group, IReadOnlyList<CatalogEntry> Samples)> groups,
        List<ResourceValidation>? detail,
        List<OmittedItem> omitted,
        List<IssueItem> issues,
        List<NextCall> next,
        CancellationToken cancellationToken)
    {
        List<ResourceOutcome>? outcomes = detail?.Where(validation => validation.Problem is null).Select(Outcome).ToList();
        int listedGroups = Math.Min(groups.Count, MaxGroups);
        int listedOutcomes = outcomes?.Count ?? 0;
        int listedIssues = issues.Count;
        int listedAbsent = scope.Absent.Count;
        while (true)
        {
            List<OmittedItem> allOmitted = [.. omitted];
            if (listedIssues < issues.Count)
            {
                allOmitted.Add(new OmittedItem($"{issues.Count - listedIssues} more issues", "size-budget"));
            }

            if (listedAbsent < scope.Absent.Count)
            {
                allOmitted.Add(new OmittedItem($"{scope.Absent.Count - listedAbsent} more references the folder does not hold", "size-budget"));
            }

            if (listedGroups < groups.Count)
            {
                allOmitted.Add(new OmittedItem($"{groups.Count - listedGroups} more issue groups (validate a narrower scope, or use detail on fewer resources)", listedGroups < MaxGroups ? "size-budget" : "limit"));
            }

            if (outcomes is not null && listedOutcomes < outcomes.Count)
            {
                allOmitted.Add(new OmittedItem($"{outcomes.Count - listedOutcomes} more outcomes, from {outcomes[listedOutcomes].Reference} on (validate them with a smaller limit or by reference)", "size-budget"));
            }

            List<(IssueGroup Group, IReadOnlyList<CatalogEntry> Samples)> listed = [.. groups.Take(listedGroups)];
            List<EvidenceItem> evidence = [.. listed.SelectMany(group => group.Samples).Distinct().Select(entry => new EvidenceItem(FhirIds.Reference(entry), entry.FilePath, entry.Line, entry.Offset))];
            ValidateFacts shown = facts with { Groups = [.. listed.Select(group => group.Group)], Outcomes = outcomes?.Take(listedOutcomes).ToList() };
            ToolEnvelope<ValidateFacts?> envelope = EnvelopeAssembler.Create<ValidateFacts?>(session, shown, scope.BuiltAt, evidence, allOmitted, [.. scope.Absent.Take(listedAbsent)], [.. issues.Take(listedIssues)], cancellationToken) with { Next = next };
            if (EnvelopeJson.SizeOf(envelope) <= EnvelopeJson.MaxBytes || (listedGroups == 0 && listedOutcomes == 0 && listedIssues == 0 && listedAbsent == 0))
            {
                return envelope;
            }

            if (listedOutcomes > 0)
            {
                listedOutcomes--;
            }
            else if (listedGroups > 0)
            {
                listedGroups = Math.Max(0, listedGroups - Math.Max(1, listedGroups / 4));
            }
            else if (listedAbsent > 0)
            {
                listedAbsent = Math.Max(0, listedAbsent - Math.Max(1, listedAbsent / 4));
            }
            else
            {
                listedIssues = Math.Max(0, listedIssues - Math.Max(1, listedIssues / 4));
            }
        }
    }

    private static ResourceOutcome Outcome(ResourceValidation validation)
    {
        var result = new ValidationResult(validation.Verdict != ResourceValidation.Invalid, validation.Raw);
        JsonNode outcome = JsonNode.Parse(result.ToOperationOutcome().SerializeToString())!;
        return new ResourceOutcome(FhirIds.Reference(validation.Entry), validation.Verdict, [.. validation.Issues.Select(issue => issue.Rule)], outcome);
    }

    private static LoadedProfile RequestedProfile(ProfileSet profiles, string canonical, string? resourceType)
    {
        (string url, string? version) = TerminologyStore.SplitCanonical(canonical);
        string named = TerminologyNames.Canonical(url, version);
        if (profiles.Find(canonical) is not { } profile)
        {
            throw ToolRequestException.Error(
                ValidationRules.ProfileNotLoaded,
                $"Profile {named} is not loaded ({profiles.Profiles.Count} StructureDefinitions are loaded from the folder{(profiles.Packages.Count == 0 ? string.Empty : $" and {string.Join(", ", profiles.Packages)}")}). Pass the package that defines it in 'packages'.");
        }

        if (!profile.Usable)
        {
            throw ToolRequestException.Error(ProfileSet.UnusableCode, $"Profile {named} from {profile.Source} is loaded but could not be built into validation checks, so nothing can be validated against it.");
        }

        if (resourceType is not null && resourceType != profile.Type)
        {
            throw ToolRequestException.Error("invalid-parameter", $"Profile {named} constrains {profile.Type}, not {resourceType}.");
        }

        return profile;
    }

    private static IEnumerable<IssueItem> NotCheckedIssues(IssueSummary summary, FolderSession session, TerminologyState terminology)
    {
        int bindings = summary.ResourcesNotChecked(StoreTerminologyService.NotCheckedRule) + summary.ResourcesNotChecked(ValidationRules.TerminologyError);
        if (bindings > 0)
        {
            string remedy = session.Terminology.Options.TxServer is null
                ? "Load the package that defines the ValueSets (terminology_load_package, or 'packages'), or start the server with --tx-server for SNOMED CT, LOINC and other systems no package carries."
                : "The terminology server could not decide them either, or was unavailable.";
            yield return new IssueItem(
                "warning",
                StoreTerminologyService.NotCheckedRule,
                $"{bindings} resources have required bindings the loaded terminology ({(terminology.Store.Sources.Count == 0 ? "none" : string.Join(", ", terminology.Store.Sources.Select(source => source.Info.Name)))}) could not check, so their verdict is unknown, not valid; facts.notChecked counts them and facts.groups says which ValueSets. {remedy}",
                Location: null);
        }

        int constraints = summary.ResourcesNotChecked(ValidationRules.ConstraintNotEvaluated);
        if (constraints > 0)
        {
            yield return new IssueItem("information", ValidationRules.ConstraintNotEvaluated, $"{constraints} resources have FHIRPath constraints the engine could not evaluate against them; their verdict is unknown unless another check failed.", Location: null);
        }

        int extensions = summary.ResourcesNotChecked(ValidationRules.ExtensionsNotValidated);
        if (extensions > 0)
        {
            yield return new IssueItem("warning", ValidationRules.ExtensionsNotValidated, $"{extensions} resources have extensions, which Ignixa.Validation does not validate (definitions, value types, bindings), so their verdict is unknown unless another check failed.", Location: null);
        }

        int spec = summary.ResourcesNotChecked(ValidationRules.DepthSpec);
        if (spec > 0)
        {
            yield return new IssueItem("information", ValidationRules.DepthSpec, $"Validated at depth spec, which does not run invariants, slicing, extensible bindings or complex choice variants: every resource is subject to invariants, so none is reported valid ({spec} unknown unless another check failed). Use depth full for a verdict.", Location: null);
        }
    }

    private static IEnumerable<IssueItem> ProfileNotLoadedIssue(List<ResourceValidation> validations)
    {
        List<(string Profile, string Reference)> missing = [.. validations
            .SelectMany(validation => validation.Issues.Where(issue => issue.Rule == ValidationRules.ProfileNotLoaded).Select(issue => (issue.Profile, FhirIds.Reference(validation.Entry))))];
        if (missing.Count == 0)
        {
            yield break;
        }

        List<string> profiles = [.. missing.Select(item => item.Profile).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
        yield return new IssueItem(
            "warning",
            ValidationRules.ProfileNotLoaded,
            $"{missing.Select(item => item.Reference).Distinct(StringComparer.Ordinal).Count()} resources claim profiles that are not loaded, so they were validated against the core definition only and their verdict is unknown: {string.Join(", ", profiles.Take(ListedProfiles))}{(profiles.Count > ListedProfiles ? $" and {profiles.Count - ListedProfiles} more" : string.Empty)}. Pass the package that defines them in 'packages'.",
            Location: null);
    }

    // Built from structure only: the request's own arguments, the next offset, and sample references (shape-checked ids).
    private static List<NextCall> Next(ValidateRequest request, int? nextOffset, IReadOnlyList<(IssueGroup Group, IReadOnlyList<CatalogEntry> Samples)> groups)
    {
        List<NextCall> next = [];
        if (nextOffset is { } offset)
        {
            next.Add(new NextCall("validate_resources", Arguments(request, offset)));
        }

        if (!request.Detail && groups.FirstOrDefault(group => group.Group.Severity is "error" or "fatal").Samples is [var first, ..])
        {
            Dictionary<string, object> args = Arguments(request with { ResourceType = null, Query = null, Patient = null, References = [FhirIds.Reference(first)], Limit = null, Detail = true }, offset: null);
            next.Add(new NextCall("validate_resources", args));
        }

        return next;
    }

    private static Dictionary<string, object> Arguments(ValidateRequest request, int? offset)
    {
        Dictionary<string, object> args = [];
        AddIf(args, "resourceType", request.ResourceType);
        AddIf(args, "query", request.Query);
        AddIf(args, "patient", request.Patient);
        if (request.References is { Count: > 0 } references)
        {
            args["references"] = references;
        }

        AddIf(args, "profile", request.Profile);
        if (request.Packages is { Count: > 0 } packages)
        {
            args["packages"] = packages;
        }

        AddIf(args, "depth", request.Depth);
        if (request.Limit is { } limit)
        {
            args["limit"] = limit;
        }

        if (request.Detail)
        {
            args["detail"] = true;
        }

        if (offset is { } value)
        {
            args["offset"] = value;
        }

        return args;
    }

    private static void AddIf(Dictionary<string, object> args, string name, string? value)
    {
        if (value is not null)
        {
            args[name] = value;
        }
    }

    private static ValidationDepth Depth(string? depth) => depth switch
    {
        null or "full" => ValidationDepth.Full,
        "spec" => ValidationDepth.Spec,
        _ => throw ToolRequestException.Error("invalid-parameter", $"'depth' must be 'spec' or 'full', not '{depth}'."),
    };

    /// <summary>The resources to validate in this call: the requested limit, at most the maximum for the mode.</summary>
    private static int Limit(ValidateRequest request)
    {
        (int fallback, int max) = request.Detail ? (DefaultDetailLimit, MaxDetailLimit) : (DefaultLimit, MaxLimit);
        if (request.Limit is < 1)
        {
            throw ToolRequestException.Error("invalid-parameter", $"'limit' must be at least 1, not {request.Limit}.");
        }

        return Math.Min(request.Limit ?? fallback, max);
    }

    /// <summary>A clamped limit (stating how many were validated) and an offset at or past the end of the scope.</summary>
    private static IEnumerable<IssueItem> PagingNotes(ValidateRequest request, int limit, int validated, int inScope)
    {
        if (request.Limit > limit)
        {
            yield return new IssueItem("warning", "limit-clamped", $"limit {request.Limit} exceeds the maximum of {limit}{(request.Detail ? " with detail" : string.Empty)}, so {validated} resources were validated; use offset to continue.", Location: null);
        }

        if (request.Offset > 0 && request.Offset >= inScope)
        {
            yield return new IssueItem("information", "offset-beyond-total", $"offset {request.Offset} is at or beyond the {inScope} resources in scope, so none were validated.", Location: null);
        }
    }

    /// <summary>The checks that do not run at <paramref name="depth"/>, as facts state them.</summary>
    private static IReadOnlyList<string> ChecksNotRun(ValidationDepth depth) => depth == ValidationDepth.Spec
        ? ["invariants", "slicing", "extensible-bindings", "complex-choice-variants", "extensions", "displays"]
        : ["extensions", "displays"];
}
