// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;
using System.Text.Json;
using Ignixa.Abstractions;
using Ignixa.CliOnFhir.Catalog;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Reading;
using Ignixa.CliOnFhir.Terminology;
using Ignixa.CliOnFhir.Terminology.Npm;
using Ignixa.PackageManagement.Infrastructure;
using Ignixa.PackageManagement.Models;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Schema;

namespace Ignixa.CliOnFhir.Validation;

/// <summary>
/// The StructureDefinitions validation can use beyond the core definitions (those of the folder, then of the requested
/// packages, later ones taking precedence for a repeated canonical), layered over the session's core schema with Ignixa's
/// <see cref="ProfileLayeredSchemaProvider"/>, and a schema resolver whose binding checks use the session's terminology
/// adapter. Immutable once built; the resolver caches the schemas it builds and is safe to use concurrently.
/// </summary>
internal sealed class ProfileSet
{
    public const string UnusableCode = "profile-unusable";

    private const string CorePrefix = "http://hl7.org/fhir/StructureDefinition/";

    private readonly Dictionary<string, LoadedProfile> _byCanonical;

    private ProfileSet(ISchema schema, IValidationSchemaResolver resolver, IReadOnlyList<LoadedProfile> profiles, IReadOnlyList<string> packages, IReadOnlyList<IssueItem> problems)
    {
        Schema = schema;
        Resolver = resolver;
        Profiles = profiles;
        Packages = packages;
        Problems = problems;

        // The last definition of a canonical wins, as in the layered provider.
        _byCanonical = new Dictionary<string, LoadedProfile>(StringComparer.Ordinal);
        foreach (LoadedProfile profile in profiles)
        {
            _byCanonical[profile.Url] = profile;
            if (profile.Version is { } version)
            {
                _byCanonical[$"{profile.Url}|{version}"] = profile;
            }
        }
    }

    /// <summary>The schema resources are read with: the core schema, with the loaded StructureDefinitions layered over it.</summary>
    public ISchema Schema { get; }

    public IValidationSchemaResolver Resolver { get; }

    /// <summary>Every StructureDefinition loaded, folder first, then the packages in request order.</summary>
    public IReadOnlyList<LoadedProfile> Profiles { get; }

    /// <summary>The packages (<c>id@version</c>) whose StructureDefinitions are loaded, in request order.</summary>
    public IReadOnlyList<string> Packages { get; }

    /// <summary>StructureDefinitions that could not be read or built, as warnings.</summary>
    public IReadOnlyList<IssueItem> Problems { get; }

    /// <summary>The loaded profile <paramref name="canonical"/> (<c>url</c> or <c>url|version</c>) names; null when none is loaded.</summary>
    public LoadedProfile? Find(string canonical) => _byCanonical.GetValueOrDefault(canonical);

    /// <summary>
    /// The resource type a target profile canonical constrains: the core definition's own type, or a loaded profile's type; null
    /// when the canonical names neither, so the type cannot be told.
    /// </summary>
    public string? TypeOf(string targetProfile)
    {
        ArgumentNullException.ThrowIfNull(targetProfile);

        string url = TerminologyStore.SplitCanonical(targetProfile).Url;
        return url.StartsWith(CorePrefix, StringComparison.Ordinal) && url[CorePrefix.Length..] is { Length: > 0 } name && !name.Contains('/', StringComparison.Ordinal)
            ? name
            : Find(targetProfile)?.Type ?? Find(url)?.Type;
    }

    /// <param name="core">The session's core schema.</param>
    /// <param name="rootPath">The folder, for reading its StructureDefinitions.</param>
    /// <param name="folderDefinitions">The folder's catalogued StructureDefinitions.</param>
    /// <param name="packages">The acquired packages, their StructureDefinitions kept.</param>
    /// <param name="terminology">The terminology the binding checks ask.</param>
    public static ProfileSet Build(IFhirSchemaProvider core, string rootPath, IReadOnlyList<CatalogEntry> folderDefinitions, IReadOnlyList<AcquiredPackage> packages, ITerminologyService terminology)
    {
        ArgumentNullException.ThrowIfNull(core);
        ArgumentNullException.ThrowIfNull(folderDefinitions);
        ArgumentNullException.ThrowIfNull(packages);

        List<IssueItem> problems = [];
        List<(ExtractedResource Resource, string Type, string Source, EvidenceItem Evidence, IReadOnlyDictionary<string, ProfileElementRule> Elements, string? BaseDefinition, bool HasSnapshot)> definitions = [];
        foreach (CatalogEntry entry in folderDefinitions)
        {
            (byte[]? json, IssueItem? problem) = ResourceReader.ReadBytes(rootPath, entry);
            if (json is null)
            {
                problems.Add(problem! with { Severity = "warning", Code = UnusableCode, Diagnostics = $"A StructureDefinition in the folder was not loaded for validation: {problem.Diagnostics}" });
                continue;
            }

            Add(definitions, problems, json, core.FullVersion, TerminologySourceInfo.FolderName, new EvidenceItem(FhirIds.Reference(entry), entry.FilePath, entry.Line, entry.Offset));
        }

        foreach (AcquiredPackage package in packages)
        {
            string source = $"{package.Id}@{package.Version}";
            foreach (TerminologyFile file in package.Contents.Profiles)
            {
                Add(definitions, problems, file.Json.ToArray(), core.FullVersion, source, new EvidenceItem("StructureDefinition", $"{package.Id}#{package.Version}/{file.Path}", Line: null, Offset: 0));
            }
        }

        // Ignixa's own layering adapts each definition (generating a snapshot from the differential when it has none); one it
        // cannot adapt is dropped there, so it is found here by asking for it back.
        ISchema schema = definitions.Count == 0 ? core : new ProfileLayeredSchemaProvider(core, [.. definitions.Select(definition => definition.Resource)]);
        List<LoadedProfile> profiles = [];
        foreach ((ExtractedResource resource, string type, string source, EvidenceItem evidence, IReadOnlyDictionary<string, ProfileElementRule> elements, _, _) in definitions)
        {
            bool usable = schema.GetTypeDefinition(resource.Canonical) is not null;
            profiles.Add(new LoadedProfile(resource.Canonical, resource.Version, type, source, usable, evidence, elements));
            if (!usable)
            {
                problems.Add(new IssueItem("warning", UnusableCode, $"StructureDefinition {TerminologyNames.Canonical(resource.Canonical, resource.Version)} from {source} could not be built into validation checks (no snapshot, or one that cannot be generated from its differential); resources claiming it are reported as profile-not-loaded.", evidence.File));
            }
        }

        InheritRules(profiles, [.. definitions.Select(definition => (definition.BaseDefinition, definition.HasSnapshot))]);
        var resolver = new CachedValidationSchemaResolver(new StructureDefinitionSchemaResolver(schema, terminologyService: terminology));
        return new ProfileSet(schema, resolver, profiles, [.. packages.Select(package => $"{package.Id}@{package.Version}")], problems);
    }

    /// <summary>
    /// Gives each profile without a snapshot the rules it inherits: those of its <c>baseDefinition</c>, followed through the
    /// loaded profiles, each overriding its parent's per element, up to a core definition or a profile with a snapshot (which
    /// states every inherited rule). A chain with a link that is not loaded (or a cycle) cannot have a snapshot generated, so
    /// Ignixa's layering leaves that profile unusable and resources claiming it are <c>profile-not-loaded</c>; it is never
    /// applied with the rules it has.
    /// </summary>
    private static void InheritRules(List<LoadedProfile> profiles, IReadOnlyList<(string? BaseDefinition, bool HasSnapshot)> links)
    {
        var byCanonical = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int index = 0; index < profiles.Count; index++)
        {
            byCanonical[profiles[index].Url] = index;
            if (profiles[index].Version is { } version)
            {
                byCanonical[$"{profiles[index].Url}|{version}"] = index;
            }
        }

        LoadedProfile[] own = [.. profiles];
        for (int index = 0; index < own.Length; index++)
        {
            List<IReadOnlyDictionary<string, ProfileElementRule>> chain = [own[index].Elements];
            HashSet<int> visited = [index];
            int current = index;
            while (!links[current].HasSnapshot && links[current].BaseDefinition is { } parent && !parent.StartsWith(CorePrefix, StringComparison.Ordinal)
                && (byCanonical.TryGetValue(parent, out int next) || byCanonical.TryGetValue(TerminologyStore.SplitCanonical(parent).Url, out next)) && visited.Add(next))
            {
                chain.Add(own[next].Elements);
                current = next;
            }

            if (chain.Count > 1)
            {
                profiles[index] = own[index] with { Elements = Merge(chain) };
            }
        }
    }

    /// <summary>The rules of a chain ordered child first: per element and per rule, the nearest definition's.</summary>
    private static Dictionary<string, ProfileElementRule> Merge(List<IReadOnlyDictionary<string, ProfileElementRule>> chain)
    {
        var merged = new Dictionary<string, ProfileElementRule>(StringComparer.Ordinal);
        foreach (IReadOnlyDictionary<string, ProfileElementRule> rules in Enumerable.Reverse(chain))
        {
            foreach ((string path, ProfileElementRule rule) in rules)
            {
                ProfileElementRule? inherited = merged.GetValueOrDefault(path);
                merged[path] = new ProfileElementRule(rule.RequiredValueSet ?? inherited?.RequiredValueSet, rule.TargetProfiles ?? inherited?.TargetProfiles);
            }
        }

        return merged;
    }

    private static void Add(List<(ExtractedResource, string, string, EvidenceItem, IReadOnlyDictionary<string, ProfileElementRule>, string?, bool)> definitions, List<IssueItem> problems, byte[] json, string fhirVersion, string source, EvidenceItem evidence)
    {
        string? url;
        string? version;
        string? type;
        string? id;
        string? baseDefinition;
        bool hasSnapshot;
        IReadOnlyDictionary<string, ProfileElementRule> elements;
        try
        {
            using var document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            url = String(root, "url");
            version = String(root, "version");
            type = String(root, "type");
            id = String(root, "id");
            baseDefinition = String(root, "baseDefinition");
            hasSnapshot = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("snapshot", out JsonElement snapshot) && snapshot.ValueKind == JsonValueKind.Object
                && snapshot.TryGetProperty("element", out JsonElement snapshotElements) && snapshotElements.ValueKind == JsonValueKind.Array && snapshotElements.GetArrayLength() > 0;
            elements = ElementRules(root);
        }
        catch (JsonException)
        {
            problems.Add(new IssueItem("warning", UnusableCode, $"A StructureDefinition from {source} is not valid JSON and was not loaded for validation.", evidence.File));
            return;
        }

        if (url is null || type is null || id is null)
        {
            problems.Add(new IssueItem("warning", UnusableCode, $"A StructureDefinition from {source} has no url, type or id and was not loaded for validation.", evidence.File));
            return;
        }

        var resource = new ExtractedResource
        {
            ResourceType = "StructureDefinition",
            Canonical = url,
            Version = version,
            ResourceId = id,
            ResourceJson = Encoding.UTF8.GetString(json),
            FhirVersion = fhirVersion,
        };
        definitions.Add((resource, type, source, evidence, elements, baseDefinition, hasSnapshot));
    }

    /// <summary>The required bindings and reference target profiles of the definition's elements: the snapshot's, then the differential's over them; slices are left out.</summary>
    private static Dictionary<string, ProfileElementRule> ElementRules(JsonElement root)
    {
        var rules = new Dictionary<string, ProfileElementRule>(StringComparer.Ordinal);
        foreach (string part in new[] { "snapshot", "differential" })
        {
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(part, out JsonElement container) || container.ValueKind != JsonValueKind.Object
                || !container.TryGetProperty("element", out JsonElement elements) || elements.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (JsonElement element in elements.EnumerateArray())
            {
                if (String(element, "path") is not { } path || (String(element, "id") is { } id && id.Contains(':', StringComparison.Ordinal)))
                {
                    continue;
                }

                string? required = element.ValueKind == JsonValueKind.Object && element.TryGetProperty("binding", out JsonElement binding)
                    && string.Equals(String(binding, "strength"), "required", StringComparison.Ordinal) ? String(binding, "valueSet") : null;
                List<string> targets = [];
                if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty("type", out JsonElement types) && types.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement typeRef in types.EnumerateArray().Where(typeRef => String(typeRef, "code") == "Reference"))
                    {
                        if (typeRef.TryGetProperty("targetProfile", out JsonElement profiles) && profiles.ValueKind == JsonValueKind.Array)
                        {
                            targets.AddRange(profiles.EnumerateArray().Where(profile => profile.ValueKind == JsonValueKind.String).Select(profile => profile.GetString()!));
                        }
                    }
                }

                ProfileElementRule? existing = rules.GetValueOrDefault(path);
                rules[path] = new ProfileElementRule(required ?? existing?.RequiredValueSet, targets.Count > 0 ? targets : existing?.TargetProfiles);
            }
        }

        return rules;
    }

    private static string? String(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text ? text : null;
}
