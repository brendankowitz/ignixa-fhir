// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Reflection;
using Ignixa.Abstractions;

namespace Ignixa.NarrativeGenerator.Engine;

/// <summary>
/// Resolves Scriban templates from embedded resources using a priority-based resolution strategy.
/// </summary>
/// <remarks>
/// <para>
/// Templates are loaded from embedded resources in the assembly with the following folder structure:
/// </para>
/// <list type="bullet">
///   <item>Templates/Normative/*.scriban - Cross-version templates for normative resources</item>
///   <item>Templates/R4/*.scriban - R4-specific templates</item>
///   <item>Templates/R5/*.scriban - R5-specific templates</item>
/// </list>
/// <para>
/// Resolution order:
/// </para>
/// <list type="number">
///   <item>Version-specific resource template (e.g., Templates/R4/Patient.scriban)</item>
///   <item>Normative resource template (e.g., Templates/Normative/Patient.scriban)</item>
///   <item>Version-specific generic template (e.g., Templates/R4/Generic.scriban)</item>
///   <item>Normative generic template (Templates/Normative/Generic.scriban)</item>
/// </list>
/// </remarks>
public class TemplateResolver : ITemplateResolver
{
    private const string TemplatesNamespacePrefix = "Ignixa.NarrativeGenerator.Templates";
    private const string TemplateExtension = ".scriban";
    private const string GenericTemplateName = "Generic";
    private const string NormativeFolder = "Normative";

    private readonly Assembly _resourceAssembly;
    private readonly ConcurrentDictionary<string, string> _templateCache = new();
    private readonly HashSet<string> _availableResources;

    /// <summary>
    /// Creates a new TemplateResolver that loads templates from the specified assembly.
    /// </summary>
    /// <param name="resourceAssembly">The assembly containing embedded template resources.</param>
    public TemplateResolver(Assembly resourceAssembly)
    {
        ArgumentNullException.ThrowIfNull(resourceAssembly);
        _resourceAssembly = resourceAssembly;

        // Cache available resource names for fast lookup
        _availableResources = [.. _resourceAssembly.GetManifestResourceNames()];
    }

    /// <summary>
    /// Creates a new TemplateResolver that loads templates from the Ignixa.NarrativeGenerator assembly.
    /// </summary>
    public TemplateResolver()
        : this(typeof(TemplateResolver).Assembly)
    {
    }

    /// <inheritdoc />
    public async Task<TemplateResolution?> ResolveTemplateAsync(
        string resourceType,
        FhirVersion fhirVersion,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resourceType);

        // Try resolution in priority order
        var candidates = GetResolutionCandidates(resourceType, fhirVersion);

        foreach (var (resourceName, templatePath, isGeneric, resolvedVersion) in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var content = await LoadTemplateContentAsync(resourceName, cancellationToken);

            if (content is not null)
            {
                return new TemplateResolution(
                    content,
                    templatePath,
                    isGeneric ? GenericTemplateName : resourceType,
                    resolvedVersion,
                    isGeneric);
            }
        }

        return null;
    }

    /// <inheritdoc />
    public bool HasTemplate(string resourceType, FhirVersion fhirVersion)
    {
        ArgumentNullException.ThrowIfNull(resourceType);

        var candidates = GetResolutionCandidates(resourceType, fhirVersion);
        return candidates.Any(c => _availableResources.Contains(c.ResourceName));
    }

    /// <summary>
    /// Gets the list of candidate resource names in priority order.
    /// </summary>
    private IEnumerable<(string ResourceName, string TemplatePath, bool IsGeneric, FhirVersion? Version)> GetResolutionCandidates(
        string resourceType,
        FhirVersion fhirVersion)
    {
        var versionFolder = GetVersionFolder(fhirVersion);

        // 1. Version-specific resource template (e.g., R4/Patient.scriban)
        yield return (
            GetResourceName(versionFolder, resourceType),
            $"{versionFolder}/{resourceType}{TemplateExtension}",
            false,
            fhirVersion);

        // 2. Normative resource template (e.g., Normative/Patient.scriban)
        yield return (
            GetResourceName(NormativeFolder, resourceType),
            $"{NormativeFolder}/{resourceType}{TemplateExtension}",
            false,
            null);

        // 3. Version-specific generic template (e.g., R4/Generic.scriban)
        yield return (
            GetResourceName(versionFolder, GenericTemplateName),
            $"{versionFolder}/{GenericTemplateName}{TemplateExtension}",
            true,
            fhirVersion);

        // 4. Normative generic template (Normative/Generic.scriban)
        yield return (
            GetResourceName(NormativeFolder, GenericTemplateName),
            $"{NormativeFolder}/{GenericTemplateName}{TemplateExtension}",
            true,
            null);
    }

    /// <summary>
    /// Constructs the embedded resource name for a template.
    /// </summary>
    /// <param name="folder">The template folder (e.g., "R4", "Normative").</param>
    /// <param name="templateName">The template name without extension (e.g., "Patient").</param>
    /// <returns>The fully qualified embedded resource name.</returns>
    private static string GetResourceName(string folder, string templateName)
    {
        // Embedded resource names use dots as path separators
        return $"{TemplatesNamespacePrefix}.{folder}.{templateName}{TemplateExtension}";
    }

    /// <summary>
    /// Maps a FHIR version to its template folder name.
    /// </summary>
    private static string GetVersionFolder(FhirVersion fhirVersion)
    {
        return fhirVersion switch
        {
            FhirVersion.R4 => "R4",
            FhirVersion.R4B => "R4", // R4B uses R4 templates
            FhirVersion.R5 => "R5",
            FhirVersion.Stu3 => "STU3",
            _ => "R4" // Default to R4
        };
    }

    /// <summary>
    /// Loads template content from an embedded resource, using cache when available.
    /// </summary>
    private async Task<string?> LoadTemplateContentAsync(string resourceName, CancellationToken cancellationToken)
    {
        // Check cache first
        if (_templateCache.TryGetValue(resourceName, out var cached))
        {
            return cached;
        }

        // Check if resource exists
        if (!_availableResources.Contains(resourceName))
        {
            return null;
        }

        // Load from embedded resource
        await using var stream = _resourceAssembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            return null;
        }

        using var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync(cancellationToken);

        // Cache the content
        _templateCache.TryAdd(resourceName, content);

        return content;
    }
}
