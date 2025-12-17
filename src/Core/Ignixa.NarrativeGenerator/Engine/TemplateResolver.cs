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
///   <item>Templates/Html/*.scriban - Cross-version HTML templates for normative resources</item>
///   <item>Templates/Html/R4/*.scriban - R4-specific HTML templates</item>
///   <item>Templates/Html/R5/*.scriban - R5-specific HTML templates</item>
///   <item>Templates/Md/*.scriban - Cross-version Markdown templates</item>
///   <item>Templates/Md/R4/*.scriban - R4-specific Markdown templates</item>
///   <item>Templates/Compact/*.scriban - Cross-version Compact templates</item>
///   <item>Templates/Compact/R4/*.scriban - R4-specific Compact templates</item>
/// </list>
/// <para>
/// Resolution order for each format:
/// </para>
/// <list type="number">
///   <item>Format-specific version resource template (e.g., Templates/Html/R4/Patient.scriban)</item>
///   <item>Format-specific normative resource template (e.g., Templates/Html/Patient.scriban)</item>
///   <item>Format-specific version generic template (e.g., Templates/Html/R4/Generic.scriban)</item>
///   <item>Format-specific normative generic template (e.g., Templates/Html/Generic.scriban)</item>
/// </list>
/// </remarks>
internal class TemplateResolver : ITemplateResolver
{
    private const string TemplatesNamespacePrefix = "Ignixa.NarrativeGenerator.Templates";
    private const string TemplateExtension = ".scriban";
    private const string GenericTemplateName = "Generic";

    // Format folder names
    private const string HtmlFolder = "Html";
    private const string MarkdownFolder = "Md";
    private const string CompactFolder = "Compact";

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
        TemplateFormat format,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resourceType);

        // Try resolution in priority order
        var candidates = GetResolutionCandidates(resourceType, fhirVersion, format);

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
                    format,
                    isGeneric);
            }
        }

        return null;
    }

    /// <inheritdoc />
    public bool HasTemplate(string resourceType, FhirVersion fhirVersion, TemplateFormat format)
    {
        ArgumentNullException.ThrowIfNull(resourceType);

        var candidates = GetResolutionCandidates(resourceType, fhirVersion, format);
        return candidates.Any(c => _availableResources.Contains(c.ResourceName));
    }

    /// <summary>
    /// Gets the list of candidate resource names in priority order for a specific format.
    /// </summary>
    private IEnumerable<(string ResourceName, string TemplatePath, bool IsGeneric, FhirVersion? Version)> GetResolutionCandidates(
        string resourceType,
        FhirVersion fhirVersion,
        TemplateFormat format)
    {
        var formatFolder = GetFormatFolder(format);
        var versionFolder = GetVersionFolder(fhirVersion);

        // 1. Format-specific version resource template (e.g., Html/R4/Patient.scriban)
        yield return (
            GetResourceName(formatFolder, versionFolder, resourceType),
            $"{formatFolder}/{versionFolder}/{resourceType}{TemplateExtension}",
            false,
            fhirVersion);

        // 2. Format-specific normative resource template (e.g., Html/Patient.scriban)
        yield return (
            GetResourceName(formatFolder, null, resourceType),
            $"{formatFolder}/{resourceType}{TemplateExtension}",
            false,
            null);

        // 3. Format-specific version generic template (e.g., Html/R4/Generic.scriban)
        yield return (
            GetResourceName(formatFolder, versionFolder, GenericTemplateName),
            $"{formatFolder}/{versionFolder}/{GenericTemplateName}{TemplateExtension}",
            true,
            fhirVersion);

        // 4. Format-specific normative generic template (e.g., Html/Generic.scriban)
        yield return (
            GetResourceName(formatFolder, null, GenericTemplateName),
            $"{formatFolder}/{GenericTemplateName}{TemplateExtension}",
            true,
            null);
    }

    /// <summary>
    /// Constructs the embedded resource name for a template.
    /// </summary>
    /// <param name="formatFolder">The format folder (e.g., "Html", "Md", "Compact").</param>
    /// <param name="versionFolder">The version folder (e.g., "R4", "R5"), or null for normative.</param>
    /// <param name="templateName">The template name without extension (e.g., "Patient").</param>
    /// <returns>The fully qualified embedded resource name.</returns>
    private static string GetResourceName(string formatFolder, string? versionFolder, string templateName)
    {
        // Embedded resource names use dots as path separators
        if (versionFolder is not null)
        {
            return $"{TemplatesNamespacePrefix}.{formatFolder}.{versionFolder}.{templateName}{TemplateExtension}";
        }
        return $"{TemplatesNamespacePrefix}.{formatFolder}.{templateName}{TemplateExtension}";
    }

    /// <summary>
    /// Maps a template format to its folder name.
    /// </summary>
    private static string GetFormatFolder(TemplateFormat format)
    {
        return format switch
        {
            TemplateFormat.Html => HtmlFolder,
            TemplateFormat.Markdown => MarkdownFolder,
            TemplateFormat.Compact => CompactFolder,
            _ => HtmlFolder // Default to Html
        };
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

    /// <inheritdoc />
    public async Task<string?> ResolveDatatypeTemplateAsync(
        string datatypeName,
        TemplateFormat format,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(datatypeName);

        var formatFolder = GetFormatFolder(format);
        var resourceName = GetDatatypeResourceName(formatFolder, datatypeName);

        return await LoadTemplateContentAsync(resourceName, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<string?> ResolveByPathAsync(string templatePath, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(templatePath);

        // Convert path format (e.g., "Html/Datatypes/Identifier") to embedded resource name
        // Path uses forward slashes, embedded resources use dots
        var normalizedPath = templatePath
            .Replace('/', '.')
            .Replace('\\', '.');

        // Ensure .scriban extension
        if (!normalizedPath.EndsWith(TemplateExtension, StringComparison.OrdinalIgnoreCase))
        {
            normalizedPath += TemplateExtension;
        }

        var resourceName = $"{TemplatesNamespacePrefix}.{normalizedPath}";

        return await LoadTemplateContentAsync(resourceName, cancellationToken);
    }

    /// <summary>
    /// Constructs the embedded resource name for a datatype template.
    /// </summary>
    /// <param name="formatFolder">The format folder (e.g., "Html", "Md", "Compact").</param>
    /// <param name="datatypeName">The datatype name (e.g., "Identifier", "HumanName").</param>
    /// <returns>The fully qualified embedded resource name.</returns>
    private static string GetDatatypeResourceName(string formatFolder, string datatypeName)
    {
        return $"{TemplatesNamespacePrefix}.{formatFolder}.Datatypes.{datatypeName}{TemplateExtension}";
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
