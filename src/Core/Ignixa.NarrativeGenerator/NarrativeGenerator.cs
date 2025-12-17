// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using System.Resources;
using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Ignixa.NarrativeGenerator.Engine;
using Ignixa.NarrativeGenerator.Engine.ScriptFunctions;
using Ignixa.NarrativeGenerator.Localization;
using Ignixa.NarrativeGenerator.Security;
using Microsoft.Extensions.Localization;

namespace Ignixa.NarrativeGenerator;

/// <summary>
/// Orchestrates the complete narrative generation pipeline for FHIR resources.
/// </summary>
/// <remarks>
/// <para>
/// This class coordinates three main components to generate safe, localized narratives
/// in multiple output formats (XHTML, Markdown, Compact):
/// </para>
/// <list type="number">
///   <item><see cref="ITemplateResolver"/> - Resolves format-specific and version-appropriate Scriban templates</item>
///   <item><see cref="NarrativeTemplateEngine"/> - Renders templates with resource context</item>
///   <item><see cref="XhtmlSanitizer"/> - Sanitizes output to prevent XSS attacks (Html format only)</item>
/// </list>
/// <para>
/// Thread-safety: This class is thread-safe and can be registered as a singleton.
/// </para>
/// </remarks>
public class FhirNarrativeGenerator : INarrativeGenerator
{
    private readonly ITemplateResolver _templateResolver;
    private readonly NarrativeTemplateEngine _templateEngine;
    private readonly XhtmlSanitizer _sanitizer;
    private readonly ISchema _schema;

    /// <summary>
    /// Creates a new FhirNarrativeGenerator with the specified dependencies.
    /// </summary>
    /// <param name="templateResolver">Resolves format-specific and version-appropriate Scriban templates.</param>
    /// <param name="templateEngine">Renders templates with resource context.</param>
    /// <param name="sanitizer">Sanitizes output to prevent XSS attacks (used for Html format only).</param>
    /// <param name="schema">The FHIR schema for determining version and structure definitions.</param>
    internal FhirNarrativeGenerator(
        ITemplateResolver templateResolver,
        NarrativeTemplateEngine templateEngine,
        XhtmlSanitizer sanitizer,
        ISchema schema)
    {
        _templateResolver = templateResolver;
        _templateEngine = templateEngine;
        _sanitizer = sanitizer;
        _schema = schema;
    }

    /// <summary>
    /// Creates a new FhirNarrativeGenerator with default configuration.
    /// </summary>
    /// <param name="schema">The FHIR schema for FHIRPath evaluation and structure definitions.</param>
    /// <param name="localizer">Optional string localizer for internationalization. If null, uses a default non-localizing implementation.</param>
    /// <returns>A fully configured narrative generator ready for use.</returns>
    /// <remarks>
    /// <para>
    /// This factory method creates all required dependencies:
    /// </para>
    /// <list type="bullet">
    ///   <item>Template resolver with embedded resource loading</item>
    ///   <item>Template engine with FHIRPath and localization support</item>
    ///   <item>XHTML sanitizer for XSS protection</item>
    /// </list>
    /// <para>
    /// <strong>Example Usage:</strong>
    /// </para>
    /// <code>
    /// var schema = SchemaProvider.GetSchema(FhirVersion.R4);
    /// var generator = FhirNarrativeGenerator.Create(schema);
    ///
    /// var narrative = await generator.GenerateNarrativeAsync(
    ///     element,
    ///     "Patient");
    /// </code>
    /// </remarks>
    public static INarrativeGenerator Create(ISchema schema, IStringLocalizer? localizer = null)
    {
        ArgumentNullException.ThrowIfNull(schema);

        // Create default localizer if not provided - use ResourceManager for NarrativeStrings.resx
        if (localizer is null)
        {
            var resourceManager = new ResourceManager(
                "Ignixa.NarrativeGenerator.NarrativeStrings",
                typeof(FhirNarrativeGenerator).Assembly);

            localizer = new ResourceManagerStringLocalizer(resourceManager);
        }

        // Create template resolver
        var templateResolver = new TemplateResolver();

        // Create template engine first (without render_resource support initially)
        var fhirPathFunctionsInitial = new FhirPathScriptFunctions(schema);
        var templateEngine = new NarrativeTemplateEngine(fhirPathFunctionsInitial, localizer, templateResolver);

        // Create FHIRPath functions with full support (including render_resource)
        var fhirPathFunctions = new FhirPathScriptFunctions(schema, templateResolver, templateEngine);

        // Recreate template engine with full FHIRPath functions and template composition support
        templateEngine = new NarrativeTemplateEngine(fhirPathFunctions, localizer, templateResolver);

        // Create sanitizer
        var sanitizer = new XhtmlSanitizer();

        // Create and return generator with schema
        return new FhirNarrativeGenerator(templateResolver, templateEngine, sanitizer, schema);
    }

    /// <inheritdoc />
    public async Task<string> GenerateNarrativeAsync(
        IElement element,
        string resourceType,
        CultureInfo? culture = null,
        TemplateFormat format = TemplateFormat.Html,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentException.ThrowIfNullOrEmpty(resourceType);

        var actualCulture = culture ?? CultureInfo.CurrentCulture;

        // Get FHIR version from the schema
        var fhirVersion = _schema.Version;

        // 1. Resolve template (format-specific → version-specific → Generic fallback)
        var resolution = await _templateResolver.ResolveTemplateAsync(resourceType, fhirVersion, format, cancellationToken);

        if (resolution is null)
        {
            throw new InvalidOperationException(
                $"No template found for resource type '{resourceType}' (FHIR version: {fhirVersion}, format: {format})");
        }

        // 2. Render template with element (already IElement - no conversion needed)
        var rendered = await _templateEngine.RenderAsync(
            resolution.Content,
            element,
            resourceType,
            fhirVersion,
            actualCulture,
            cancellationToken);

        // 3. Sanitize output for XSS protection (Html format only)
        // Markdown and Embedding formats return raw template output without HTML sanitization
        var output = format == TemplateFormat.Html
            ? _sanitizer.Sanitize(rendered)
            : rendered;

        return output;
    }
}
