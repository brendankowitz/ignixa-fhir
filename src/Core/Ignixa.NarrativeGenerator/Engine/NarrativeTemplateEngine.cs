// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json.Nodes;
using Ignixa.Abstractions;
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
///   <item>Template composition via includes (using ITemplateLoader)</item>
/// </list>
/// <para>
/// Thread-safety: This class is thread-safe and can be shared across multiple requests.
/// The template cache uses ConcurrentDictionary for safe concurrent access.
/// </para>
/// </remarks>
internal class NarrativeTemplateEngine
{
    private readonly ConcurrentDictionary<string, Template> _compiledTemplateCache = new();
    private readonly FhirPathScriptFunctions _fhirPathFunctions;
    private readonly LocalizationScriptFunctions? _localizationFunctions;
    private readonly ScribanTemplateLoader? _templateLoader;

    /// <summary>
    /// Creates a new NarrativeTemplateEngine with the specified FHIRPath functions and optional localization.
    /// </summary>
    /// <param name="fhirPathFunctions">FHIRPath script functions for template evaluation.</param>
    /// <param name="stringLocalizer">Optional string localizer for narrative text localization.</param>
    public NarrativeTemplateEngine(
        FhirPathScriptFunctions fhirPathFunctions,
        IStringLocalizer? stringLocalizer = null)
        : this(fhirPathFunctions, stringLocalizer, templateResolver: null)
    {
    }

    /// <summary>
    /// Creates a new NarrativeTemplateEngine with template composition support.
    /// </summary>
    /// <param name="fhirPathFunctions">FHIRPath script functions for template evaluation.</param>
    /// <param name="stringLocalizer">Optional string localizer for narrative text localization.</param>
    /// <param name="templateResolver">Optional template resolver for include directive support.</param>
    /// <remarks>
    /// When a templateResolver is provided, templates can use the include directive to compose
    /// from datatype sub-templates:
    /// <code>
    /// {{~ include "Html/Datatypes/Identifier" ~}}
    /// </code>
    /// </remarks>
    public NarrativeTemplateEngine(
        FhirPathScriptFunctions fhirPathFunctions,
        IStringLocalizer? stringLocalizer,
        ITemplateResolver? templateResolver)
    {
        ArgumentNullException.ThrowIfNull(fhirPathFunctions);

        _fhirPathFunctions = fhirPathFunctions;
        _localizationFunctions = stringLocalizer is not null
            ? new LocalizationScriptFunctions(stringLocalizer)
            : null;
        _templateLoader = templateResolver is not null
            ? new ScribanTemplateLoader(templateResolver)
            : null;
    }

    /// <summary>
    /// Renders a narrative for the given FHIR resource using the specified template.
    /// </summary>
    /// <param name="template">The Scriban template to render.</param>
    /// <param name="resource">The FHIR resource element to render.</param>
    /// <param name="resourceType">The FHIR resource type.</param>
    /// <param name="fhirVersion">The FHIR version of the resource.</param>
    /// <param name="culture">The culture for localization.</param>
    /// <param name="cancellationToken">Cancellation token for async operations.</param>
    /// <returns>The rendered HTML narrative content.</returns>
    /// <exception cref="ArgumentNullException">Thrown when template or resource is null.</exception>
    /// <exception cref="TemplateRenderException">Thrown when template rendering fails.</exception>
    public async Task<string> RenderAsync(
        Template template,
        IElement resource,
        string resourceType,
        FhirVersion fhirVersion,
        CultureInfo culture,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(resourceType);
        ArgumentNullException.ThrowIfNull(culture);

        var context = CreateTemplateContext(resource, resourceType, fhirVersion, culture);

        try
        {
            var result = await template.RenderAsync(context);
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new TemplateRenderException(
                $"Failed to render template for resource type '{resourceType}'",
                ex);
        }
    }

    /// <summary>
    /// Renders a narrative for the given FHIR resource using the specified template content.
    /// </summary>
    /// <param name="templateContent">The Scriban template content to render.</param>
    /// <param name="resource">The FHIR resource element to render.</param>
    /// <param name="resourceType">The FHIR resource type.</param>
    /// <param name="fhirVersion">The FHIR version of the resource.</param>
    /// <param name="culture">The culture for localization.</param>
    /// <param name="cancellationToken">Cancellation token for async operations.</param>
    /// <returns>The rendered HTML narrative content.</returns>
    public async Task<string> RenderAsync(
        string templateContent,
        IElement resource,
        string resourceType,
        FhirVersion fhirVersion,
        CultureInfo culture,
        CancellationToken cancellationToken)
    {
        var template = ParseOrGetCached(templateContent);
        return await RenderAsync(template, resource, resourceType, fhirVersion, culture, cancellationToken);
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
    private TemplateContext CreateTemplateContext(IElement resource, string resourceType, FhirVersion fhirVersion, CultureInfo culture)
    {
        var context = new TemplateContext
        {
            // Enable HTML auto-escaping for XSS protection
            AutoIndent = false,
            MemberRenamer = member => member.Name,
            // Configure template loader for include directive support
            TemplateLoader = _templateLoader
        };

        // CRITICAL: Set the culture for the entire template context using PushCulture
        // This ensures all Scriban built-in functions (date formatting, number formatting, etc.)
        // use the specified culture consistently with our localized strings
        // See: https://github.com/scriban/scriban/blob/master/src/Scriban/TemplateContext.cs
        context.PushCulture(culture);

        // Create root ScriptObject with resource data
        var scriptObject = new ScriptObject();

        // Extract resource ID using FHIRPath
        var resourceId = _fhirPathFunctions.Path(resource, "id") ?? string.Empty;

        // Add the resource as the main context variable
        scriptObject.SetValue("resource", resource, readOnly: true);
        scriptObject.SetValue("resourceType", resourceType, readOnly: true);
        scriptObject.SetValue("resourceId", resourceId, readOnly: true);
        scriptObject.SetValue("fhirVersion", fhirVersion, readOnly: true);

        // Register FHIRPath functions using Import(name, delegate) method
        // Wrap in lambdas to ensure Scriban can inspect parameters correctly
        scriptObject.Import("fhirpath", new Func<object, object, string?>((resource, expression) => _fhirPathFunctions.FhirPath(resource, expression)));
        scriptObject.Import("path", new Func<object, object, string?>((resource, expression) => _fhirPathFunctions.Path(resource, expression)));
        scriptObject.Import("path_first", new Func<object, object, string?>((resource, expression) => _fhirPathFunctions.PathFirst(resource, expression)));
        scriptObject.Import("path_all", new Func<object, object, IEnumerable<string>>((resource, expression) => _fhirPathFunctions.PathAll(resource, expression)));
        scriptObject.Import("path_element", new Func<object, object, IElement?>((resource, expression) => _fhirPathFunctions.PathElement(resource, expression)));
        scriptObject.Import("exists", new Func<object, object, bool>((resource, expression) => _fhirPathFunctions.Exists(resource, expression)));
        scriptObject.Import("count", new Func<object, object, int>((resource, expression) => _fhirPathFunctions.Count(resource, expression)));

        // Use lambdas to handle optional parameters (culture defaults to context culture)
        scriptObject.Import("format_date", (Func<string?, string>)(date => _fhirPathFunctions.FormatDate(date, culture)));
        scriptObject.Import("format_datetime", (Func<string?, string>)(datetime => _fhirPathFunctions.FormatDateTime(datetime, culture)));

        scriptObject.Import("display", (Func<JsonNode?, string>)FhirPathScriptFunctions.Display);
        scriptObject.Import("display_coding", (Func<JsonNode?, string>)FhirPathScriptFunctions.DisplayCoding);
        scriptObject.Import("display_reference", (Func<JsonNode?, string>)FhirPathScriptFunctions.DisplayReference);
        scriptObject.Import("display_quantity", (Func<JsonNode?, string>)FhirPathScriptFunctions.DisplayQuantity);
        scriptObject.Import("is_empty", (Func<JsonNode?, bool>)FhirPathScriptFunctions.IsEmpty);
        scriptObject.Import("safe_html", (Func<string?, string>)FhirPathScriptFunctions.SafeHtml);

        // Metadata helpers for Generic template
        scriptObject.Import("get_structure_elements", new Func<string, object, IEnumerable<ElementMetadata>>((resourceType, fhirVersion) => _fhirPathFunctions.GetStructureElements(resourceType, fhirVersion)));
        scriptObject.Import("format_by_type", new Func<string?, string, string>((value, type) => _fhirPathFunctions.FormatByType(value, type, culture)));
        scriptObject.Import("get_element_name", (Func<object?, string>)FhirPathScriptFunctions.GetElementName);

        // Register render_resource for nested rendering (if available)
        scriptObject.Import("render_resource", new Func<IElement, string, object, string, string, string>(
            (res, resType, ver, fmt, cult) => _fhirPathFunctions.RenderResource(res, resType, ver, fmt, cult)));

        // Also expose under 'fhir' namespace for template compatibility
        var fhirObject = new ScriptObject();
        fhirObject.Import("path", new Func<object, object, string?>((resource, expression) => _fhirPathFunctions.Path(resource, expression)));
        fhirObject.Import("path_element", new Func<object, object, IElement?>((resource, expression) => _fhirPathFunctions.PathElement(resource, expression)));
        fhirObject.Import("exists", new Func<object, object, bool>((resource, expression) => _fhirPathFunctions.Exists(resource, expression)));
        fhirObject.Import("count", new Func<object, object, int>((resource, expression) => _fhirPathFunctions.Count(resource, expression)));
        fhirObject.Import("format_date", (Func<string?, string>)(date => _fhirPathFunctions.FormatDate(date, culture)));
        fhirObject.Import("format_datetime", (Func<string?, string>)(datetime => _fhirPathFunctions.FormatDateTime(datetime, culture)));
        fhirObject.Import("calculate_age", (Func<string?, string>)FhirPathScriptFunctions.CalculateAge);
        fhirObject.Import("display", (Func<JsonNode?, string>)FhirPathScriptFunctions.Display);
        fhirObject.Import("code_display", new Func<string?, string?, string>((system, code) => _fhirPathFunctions.CodeDisplay(system, code)));
        fhirObject.Import("get_structure_elements", new Func<string, object, IEnumerable<ElementMetadata>>((resourceType, fhirVersion) => _fhirPathFunctions.GetStructureElements(resourceType, fhirVersion)));
        fhirObject.Import("format_by_type", new Func<string?, string, string>((value, type) => _fhirPathFunctions.FormatByType(value, type, culture)));
        fhirObject.Import("get_element_name", (Func<object?, string>)FhirPathScriptFunctions.GetElementName);
        fhirObject.Import("render_resource", new Func<IElement, string, object, string, string, string>(
            (res, resType, ver, fmt, cult) => _fhirPathFunctions.RenderResource(res, resType, ver, fmt, cult)));
        scriptObject.SetValue("fhir", fhirObject, readOnly: true);

        // Register localization functions if available
        if (_localizationFunctions is not null)
        {
            scriptObject.Import("t", (Func<string, string>)_localizationFunctions.T);
            scriptObject.Import("format", (Func<string, object[], string>)_localizationFunctions.Format);
            scriptObject.Import("get_or_default", (Func<string, string, string>)_localizationFunctions.GetOrDefault);

            // Also expose under 'l10n' namespace for template compatibility
            var l10nObject = new ScriptObject();
            l10nObject.Import("t", (Func<string, string>)_localizationFunctions.T);
            l10nObject.Import("format", (Func<string, object[], string>)_localizationFunctions.Format);
            l10nObject.Import("get_or_default", (Func<string, string, string>)_localizationFunctions.GetOrDefault);
            scriptObject.SetValue("l10n", l10nObject, readOnly: true);
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
