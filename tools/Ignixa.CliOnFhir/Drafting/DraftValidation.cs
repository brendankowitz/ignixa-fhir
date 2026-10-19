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
using Ignixa.CliOnFhir.Validation;
using Ignixa.Validation;
using Ignixa.Validation.Abstractions;

namespace Ignixa.CliOnFhir.Drafting;

/// <summary>
/// Validates a draft as <c>validate</c> validates a resource of the folder (<see cref="ResourceCheck"/>, depth full): against its
/// core definition and the requested profile, with every required and extensible binding checked by the session's terminology
/// (and <c>--tx-server</c> when given), whose undecided bindings make the verdict <c>unknown</c>. The validator's messages quote
/// the resource, so only its rules, paths, constraint keys and ValueSets are kept, with the tool's own description of each rule.
/// </summary>
internal static class DraftValidation
{
    /// <summary>The most validator issues listed; the rest are counted.</summary>
    public const int MaxIssues = 30;

    /// <summary>
    /// Loads <paramref name="packages"/> (their StructureDefinitions and terminology, as <c>validate --package</c> does; copied from
    /// <c>ResourceValidator.ExecuteAsync</c>) and returns the profile set over the folder's StructureDefinitions and theirs.
    /// </summary>
    /// <exception cref="ToolRequestException">A package cannot be read or found.</exception>
    public static async Task<ProfileSet> ProfilesAsync(FolderSession session, IReadOnlyList<string> packages, CancellationToken cancellationToken)
    {
        List<AcquiredPackage> acquired = [];
        foreach (PackageRequest request in packages.Select(PackageRequest.Parse).Distinct())
        {
            AcquiredPackage package = await session.Terminology.Acquisition.AcquireAsync(request, cancellationToken, keepStructureDefinitions: true);
            await session.Terminology.LoadAcquiredAsync(package, cancellationToken);
            acquired.Add(package);
        }

        return session.Validators.For(acquired);
    }

    /// <summary>
    /// The loaded profile <paramref name="canonical"/> names, usable and of <paramref name="resourceType"/>, as <c>validate --profile</c>
    /// requires it (copied from <c>ResourceValidator.RequestedProfile</c>).
    /// </summary>
    /// <exception cref="ToolRequestException"><c>profile-not-loaded</c>, <c>profile-unusable</c> or <c>invalid-parameter</c>.</exception>
    /// <param name="profiles">The loaded profiles.</param>
    /// <param name="canonical">The profile asked for.</param>
    /// <param name="resourceType">The draft's type.</param>
    /// <param name="packageArgument">How the caller names a package to load: <c>--package</c> on the CLI, <c>input.packages</c> over MCP.</param>
    public static LoadedProfile Profile(ProfileSet profiles, string canonical, string resourceType, string packageArgument = "--package")
    {
        (string url, string? version) = TerminologyStore.SplitCanonical(canonical);
        string named = TerminologyNames.Canonical(url, version);
        if (profiles.Find(canonical) is not { } profile)
        {
            throw ToolRequestException.Error(
                ValidationRules.ProfileNotLoaded,
                $"Profile {named} is not loaded ({profiles.Profiles.Count} StructureDefinitions are loaded from the folder{(profiles.Packages.Count == 0 ? string.Empty : $" and {string.Join(", ", profiles.Packages)}")}). Pass the package that defines it (id@version) with {packageArgument}.");
        }

        if (!profile.Usable)
        {
            throw ToolRequestException.Error(ProfileSet.UnusableCode, $"Profile {named} from {profile.Source} is loaded but could not be built into validation checks, so the draft cannot be validated against it.");
        }

        return profile.Type == resourceType
            ? profile
            : throw ToolRequestException.Error("invalid-parameter", $"Profile {named} constrains {profile.Type}, not {resourceType}.");
    }

    /// <summary>The validation check of <paramref name="resource"/>, and a warning when its verdict is unknown.</summary>
    public static (DraftCheck Check, IssueItem? Note) Check(FolderSession session, ProfileSet profiles, LoadedProfile? profile, JsonObject resource, string resourceType, string id)
    {
        session.Validators.Terminology.BeginCall();
        var check = new ResourceCheck(
            session.RootPath,
            profiles,
            new ValidationSettings { Depth = ValidationDepth.Full, TerminologyService = session.Validators.Terminology },
            profile,
            (_, _) => Indexing.ReferenceResolution.None.Resolved);

        // A draft is in no file; the entry only names it.
        var entry = new CatalogEntry(resourceType, id, FilePath: "(draft)", Offset: 0, Length: 0, Line: null, HasSyntheticId: false);
        ResourceValidation validation = check.Validate(entry, resource);
        List<DraftIssueGroup> groups = [.. validation.Issues
            .Distinct()
            .Select(issue => new DraftIssueGroup(issue.Severity, issue.Profile, issue.Rule, issue.Constraint, issue.ValueSet, issue.Path, RuleDescriptions.Describe(issue.Rule, issue.Constraint, issue.Profile)))];
        var details = new ValidationCheckDetails(
            validation.Verdict,
            profile is null ? null : TerminologyNames.Canonical(profile.Url, profile.Version),
            profiles.Packages,
            [.. groups.Take(MaxIssues)],
            Math.Max(0, groups.Count - MaxIssues));
        string outcome = validation.Verdict switch
        {
            ResourceValidation.Valid => DraftCheck.Pass,
            ResourceValidation.Invalid => DraftCheck.Fail,
            _ => DraftCheck.Unknown,
        };

        IssueItem? note = outcome switch
        {
            DraftCheck.Fail => new IssueItem("error", "draft-invalid", $"The draft fails {validation.Issues.Count(issue => issue.Fails)} validator checks (rules {Rules(validation.Issues.Where(issue => issue.Fails))}); facts.checks[0].details.issues lists them. Correct the input and draft it again.", Location: null),
            DraftCheck.Unknown => new IssueItem("warning", "draft-not-fully-validated", $"The validator could not conclude every check (rules {Rules(validation.Issues.Where(issue => issue.Unevaluated))}), so the draft is not reported valid. Load the terminology or package that decides them (--terminology, --package), or use --tx-server for code systems no package carries.", Location: null),
            _ => null,
        };

        return (new DraftCheck("validation", resourceType, outcome, details), note);
    }

    private static string Rules(IEnumerable<ClassifiedIssue> issues) =>
        string.Join(", ", issues.Select(issue => issue.Rule).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
}
