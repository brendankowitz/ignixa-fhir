// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Globalization;
using Ignixa.NarrativeGenerator.Engine.ScriptFunctions;
using Ignixa.Serialization.SourceNodes;
using Microsoft.Extensions.Localization;
using Scriban;
using Scriban.Runtime;

namespace Ignixa.NarrativeGenerator.Engine;

/// <summary>
/// Core template engine for rendering FHIR resource narratives using Scriban templates.
/// </summary>
/// <remarks>
/// <para>
/// This engine provides:
/// </para>
/// <list type="bullet">
///   <item>Compiled template caching for performance</item>
///   <item>HTML auto-escaping for XSS protection</item>
///   <item>Custom FHIRPath helper functions</item>
///   <item>Localization support via IStringLocalizer</item>
/// </list>
/// <para>
/// Thread-safety: This class is thread-safe and can be shared across multiple requests.
/// The template cache uses ConcurrentDictionary for safe concurrent access.
/// </para>
/// </remarks>
public class NarrativeTemplateEngine
{
    private readonly ConcurrentDictionary<string, Template> _compiledTemplateCache = new();
    private readonly FhirPathScriptFunctions _fhirPathFunctions;
    private readonly LocalizationScriptFunctions? _localizationFunctions;

    /// <summary>
    /// Creates a new NarrativeTemplateEngine with the specified FHIRPath functions and optional localization.
    /// </summary>
    /// <param name="fhirPathFunctions">FHIRPath script functions for template evaluation.</param>
    /// <param name="stringLocalizer">Optional string localizer for narrative text localization.</param>
    public NarrativeTemplateEngine(
        FhirPathScriptFunctions fhirPathFunctions,
        IStringLocalizer? stringLocalizer = null)
    {
        ArgumentNullException.ThrowIfNull(fhirPathFunctions);

        _fhirPathFunctions = fhirPathFunctions;
        _localizationFunctions = stringLocalizer is not null
            ? new LocalizationScriptFunctions(stringLocalizer)
            : null;
    }

    /// <summary>
    /// Renders a narrative for the given FHIR resource using the specified template.
    /// </summary>
    /// <param name="template">The Scriban template to render.</param>
    /// <param name="resource">The FHIR resource to render.</param>
    /// <param name="culture">The culture for localization.</param>
    /// <param name="cancellationToken">Cancellation token for async operations.</param>
    /// <returns>The rendered HTML narrative content.</returns>
    /// <exception cref="ArgumentNullException">Thrown when template or resource is null.</exception>
    /// <exception cref="TemplateRenderException">Thrown when template rendering fails.</exception>
    public async Task<string> RenderAsync(
        Template template,
        ResourceJsonNode resource,
        CultureInfo culture,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(culture);

        var context = CreateTemplateContext(resource, culture);

        try
        {
            var result = await template.RenderAsync(context);
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new TemplateRenderException(
                $"Failed to render template for resource type '{resource.ResourceType}'",
                ex);
        }
    }

    /// <summary>
    /// Renders a narrative for the given FHIR resource using the specified template content.
    /// </summary>
    /// <param name="templateContent">The Scriban template content to render.</param>
    /// <param name="resource">The FHIR resource to render.</param>
    /// <param name="culture">The culture for localization.</param>
    /// <param name="cancellationToken">Cancellation token for async operations.</param>
    /// <returns>The rendered HTML narrative content.</returns>
    public async Task<string> RenderAsync(
        string templateContent,
        ResourceJsonNode resource,
        CultureInfo culture,
        CancellationToken cancellationToken)
    {
        var template = ParseOrGetCached(templateContent);
        return await RenderAsync(template, resource, culture, cancellationToken);
    }

    /// <summary>
    /// Parses a Scriban template string, using cache when available.
    /// </summary>
    /// <param name="templateContent">The template content to parse.</param>
    /// <returns>The parsed and compiled Scriban template.</returns>
    /// <exception cref="ArgumentException">Thrown when the template content is invalid.</exception>
    public Template ParseOrGetCached(string templateContent)
    {
        ArgumentNullException.ThrowIfNull(templateContent);

        // Use content hash as cache key
        var cacheKey = GetCacheKey(templateContent);

        return _compiledTemplateCache.GetOrAdd(cacheKey, _ =>
        {
            var template = Template.Parse(templateContent);

            if (template.HasErrors)
            {
                var errors = string.Join("; ", template.Messages.Select(m => m.ToString()));
                throw new ArgumentException($"Template parsing failed: {errors}", nameof(templateContent));
            }

            return template;
        });
    }

    /// <summary>
    /// Clears the compiled template cache.
    /// </summary>
    /// <remarks>
    /// Use this method when templates have been updated and need to be reloaded.
    /// </remarks>
    public void ClearCache()
    {
        _compiledTemplateCache.Clear();
    }

    /// <summary>
    /// Creates a TemplateContext configured with resource data and custom functions.
    /// </summary>
    private TemplateContext CreateTemplateContext(ResourceJsonNode resource, CultureInfo culture)
    {
        var context = new TemplateContext
        {
            // Enable HTML auto-escaping for XSS protection
            AutoIndent = false,
            MemberRenamer = member => member.Name
        };

        // CRITICAL: Set the culture for the entire template context using PushCulture
        // This ensures all Scriban built-in functions (date formatting, number formatting, etc.)
        // use the specified culture consistently with our localized strings
        // See: https://github.com/scriban/scriban/blob/master/src/Scriban/TemplateContext.cs
        context.PushCulture(culture);

        // Create root ScriptObject with resource data
        var scriptObject = new ScriptObject();

        // Add the resource as the main context variable
        scriptObject.SetValue("resource", resource, readOnly: true);
        scriptObject.SetValue("resourceType", resource.ResourceType, readOnly: true);
        scriptObject.SetValue("resourceId", resource.Id, readOnly: true);

        // Import FHIRPath functions directly (makes them available as bare functions like fhirpath, format_date, etc.)
        scriptObject.Import(_fhirPathFunctions);

        // Also expose under 'fhir' namespace for clarity
        scriptObject.SetValue("fhir", _fhirPathFunctions, readOnly: true);

        // Import localization functions if available
        if (_localizationFunctions is not null)
        {
            scriptObject.Import(_localizationFunctions);
            scriptObject.SetValue("l10n", _localizationFunctions, readOnly: true);
        }

        // Add culture information
        scriptObject.SetValue("culture", culture.Name, readOnly: true);
        scriptObject.SetValue("lang", culture.TwoLetterISOLanguageName, readOnly: true);

        // Push the script object onto the context
        context.PushGlobal(scriptObject);

        return context;
    }

    /// <summary>
    /// Generates a cache key from template content.
    /// </summary>
    private static string GetCacheKey(string templateContent)
    {
        // Use a simple hash for cache key
        return templateContent.GetHashCode(StringComparison.Ordinal).ToString(CultureInfo.InvariantCulture);
    }
}

/// <summary>
/// Exception thrown when template rendering fails.
/// </summary>
public class TemplateRenderException : Exception
{
    /// <summary>
    /// Creates a new TemplateRenderException.
    /// </summary>
    public TemplateRenderException()
    {
    }

    /// <summary>
    /// Creates a new TemplateRenderException with the specified message.
    /// </summary>
    /// <param name="message">The error message.</param>
    public TemplateRenderException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Creates a new TemplateRenderException with the specified message and inner exception.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception that caused this error.</param>
    public TemplateRenderException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
