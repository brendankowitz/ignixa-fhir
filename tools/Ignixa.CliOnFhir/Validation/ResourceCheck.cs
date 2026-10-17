// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.CliOnFhir.Catalog;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Reading;
using Ignixa.CliOnFhir.Terminology;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Validation;
using Ignixa.Validation.Abstractions;

namespace Ignixa.CliOnFhir.Validation;

/// <summary>
/// Validates one resource against its core definition and its profiles: the requested one, or else each <c>meta.profile</c>
/// that is loaded (one that is not is reported as <see cref="ValidationRules.ProfileNotLoaded"/>). Each schema is run on its
/// own, so every issue names the StructureDefinition that raised it; an issue a profile repeats from the core definition
/// is counted once, under the core definition. Each resource is validated with its own settings, whose
/// <see cref="ValidationSettings.ReferenceTypeResolver"/> answers the urns the folder's Bundles resolve; the references
/// whose target type the validator could not tell are counted, not grouped. At depth <c>spec</c> a
/// <see cref="ValidationRules.DepthSpec"/> note keeps every verdict from reading <c>valid</c>.
/// </summary>
internal sealed class ResourceCheck(string rootPath, ProfileSet profiles, ValidationSettings settings, LoadedProfile? requested, Func<CatalogEntry, JsonObject, IReadOnlyDictionary<string, string>> bundleReferences)
{
    private const string CorePrefix = "http://hl7.org/fhir/StructureDefinition/";

    public ResourceValidation Validate(CatalogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        (byte[]? json, IssueItem? problem) = ResourceReader.ReadBytes(rootPath, entry);
        JsonNode? node = null;
        if (json is not null)
        {
            try
            {
                node = JsonNode.Parse(json);
            }
            catch (JsonException ex)
            {
                problem = new IssueItem("error", "invalid-resource", $"{ResourceReader.Describe(entry)} is not valid JSON ({ex.GetType().Name}); it was not validated.", ResourceReader.Describe(entry));
            }
        }

        if (node is not JsonObject resource)
        {
            return new ResourceValidation(entry, ResourceValidation.Unreadable, [], [], problem ?? new IssueItem("error", "invalid-resource", $"{ResourceReader.Describe(entry)} is not a JSON object; it was not validated.", Location: null));
        }

        return Validate(entry, resource);
    }

    /// <summary>Validates <paramref name="resource"/>, already read (or built, as a draft is), as the resource <paramref name="entry"/> names.</summary>
    public ResourceValidation Validate(CatalogEntry entry, JsonObject resource)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(resource);

        IElement element = JsonNodeSourceNode.Create(resource).ToElement(profiles.Schema);
        string coreUrl = CorePrefix + entry.ResourceType;
        ValidationSettings resourceSettings = ForResource(bundleReferences(entry, resource));
        UncheckedReferences references = new();
        List<ClassifiedIssue> classified = [];
        List<ValidationIssue> raw = [];
        HashSet<(IssueSeverity, string, string, string)> seen = [];

        if (profiles.Resolver.GetSchema(coreUrl) is not { } core)
        {
            ValidationIssue missing = new(IssueSeverity.Warning, ValidationRules.SchemaNotFound, entry.ResourceType, $"No validation schema is available for {entry.ResourceType}, so it was not validated.");
            return new ResourceValidation(entry, ResourceValidation.Unknown, [IssueClassifier.Classify(missing, coreUrl) with { Rule = ValidationRules.SchemaNotFound }], [missing], null);
        }

        Run(core, coreUrl, element, resourceSettings, references, classified, raw, seen);
        foreach (string canonical in ProfilesOf(resource, coreUrl))
        {
            LoadedProfile? profile = profiles.Find(canonical);
            string named = TerminologyNames.Canonical(TerminologyStore.SplitCanonical(canonical).Url, TerminologyStore.SplitCanonical(canonical).Version);
            if (profile is not null && profile.Type != entry.ResourceType)
            {
                Add(new ValidationIssue(IssueSeverity.Error, ValidationRules.ProfileTypeMismatch, entry.ResourceType, $"meta.profile names {named}, a profile of {profile.Type}, not of {entry.ResourceType}."), named, ValidationRules.ProfileTypeMismatch, classified, raw);
                continue;
            }

            if (profile is not { Usable: true } || profiles.Resolver.GetSchema(canonical) is not { } schema)
            {
                Add(new ValidationIssue(IssueSeverity.Warning, ValidationRules.ProfileNotLoaded, entry.ResourceType, $"Profile {named} is not loaded{(profile is null ? string.Empty : " in a usable form")}, so the resource was not validated against it."), named, ValidationRules.ProfileNotLoaded, classified, raw);
                continue;
            }

            Run(schema, named, element, resourceSettings, references, classified, raw, seen);
        }

        if (settings.Depth == ValidationDepth.Spec)
        {
            ValidationIssue spec = new(IssueSeverity.Information, ValidationRules.DepthSpec, entry.ResourceType, $"{ValidationRules.DepthSpec}: invariants, slicing, extensible bindings and extension definitions were not checked at depth spec");
            raw.Add(spec);
            classified.Add(new ClassifiedIssue("information", coreUrl, ValidationRules.DepthSpec, null, null, entry.ResourceType));
        }

        return new ResourceValidation(entry, ResourceValidation.VerdictOf(classified), classified, raw, null) { UndeterminableReferences = references.Undeterminable.Count, DisplayOnlyReferences = references.DisplayOnly.Count };
    }

    /// <summary>The call's settings, with a resolver for the urns <paramref name="resolved"/> maps to the <c>Type/id</c> they name in the folder.</summary>
    private ValidationSettings ForResource(IReadOnlyDictionary<string, string> resolved) => new()
    {
        Depth = settings.Depth,
        TerminologyService = settings.TerminologyService,
        TerminologyFailureMode = settings.TerminologyFailureMode,
        SkipTerminologyValidation = settings.SkipTerminologyValidation,
        ReferenceTypeResolver = resolved.Count == 0 ? null : new BundleReferenceTypes(resolved),
    };

    /// <summary>The requested profile, or else the resource's <c>meta.profile</c> canonicals, each once, without its core definition.</summary>
    private IEnumerable<string> ProfilesOf(JsonObject resource, string coreUrl)
    {
        if (requested is not null)
        {
            return [requested.Version is null ? requested.Url : $"{requested.Url}|{requested.Version}"];
        }

        IEnumerable<string> declared = resource["meta"] is JsonObject { } meta && meta["profile"] is JsonArray list
            ? list.Select(item => item is JsonValue value && value.TryGetValue(out string? text) ? text : null).OfType<string>()
            : [];
        return declared.Where(canonical => canonical.Length > 0 && TerminologyStore.SplitCanonical(canonical).Url != coreUrl).Distinct(StringComparer.Ordinal);
    }

    /// <summary>
    /// Runs one schema, keeping each issue once. A reference whose target type the validator could not tell is counted (once per
    /// element, whichever schema reported it) and kept in the full outcome, but not grouped: it leaves the verdict as it is.
    /// </summary>
    private static void Run(ValidationSchema schema, string profile, IElement element, ValidationSettings settings, UncheckedReferences references, List<ClassifiedIssue> classified, List<ValidationIssue> raw, HashSet<(IssueSeverity, string, string, string)> seen)
    {
        foreach (ValidationIssue issue in schema.Validate(element, settings).Issues)
        {
            if (!seen.Add((issue.Severity, issue.Code, issue.Path, issue.Message)))
            {
                continue;
            }

            raw.Add(issue);
            if (issue.Code == ValidationRules.ReferenceTargetUnchecked)
            {
                (issue.TemplateId == IssueTemplates.ReferenceTargetDisplayOnly.Id ? references.DisplayOnly : references.Undeterminable).Add(issue.Path);
                continue;
            }

            classified.Add(IssueClassifier.Classify(issue, profile));
        }
    }

    private static void Add(ValidationIssue issue, string profile, string rule, List<ClassifiedIssue> classified, List<ValidationIssue> raw)
    {
        raw.Add(issue);
        classified.Add(IssueClassifier.Classify(issue, profile) with { Rule = rule });
    }
}
