// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using Ignixa.Abstractions;

namespace Ignixa.NarrativeGenerator;

/// <summary>
/// Generates narrative content for FHIR resources using Scriban templates.
/// Supports multiple output formats: XHTML, Markdown, and Compact.
/// </summary>
public interface INarrativeGenerator
{
    /// <summary>
    /// Generates narrative content for a FHIR resource in the specified format.
    /// </summary>
    /// <param name="element">The FHIR resource element to generate narrative for. Must be created with an appropriate <see cref="ISchema"/> that matches the generator's configured FHIR version.</param>
    /// <param name="resourceType">The FHIR resource type (e.g., "Patient", "Observation"). This is required because <see cref="IElement"/> doesn't carry type information.</param>
    /// <param name="culture">The culture for localization (defaults to current culture).</param>
    /// <param name="format">The output format for the narrative (defaults to Html for FHIR-compliant XHTML).</param>
    /// <param name="cancellationToken">Cancellation token for async operations.</param>
    /// <returns>
    /// Narrative content in the specified format:
    /// <list type="bullet">
    ///   <item><see cref="TemplateFormat.Html"/>: Sanitized XHTML narrative content (WCAG 2.1 AA compliant)</item>
    ///   <item><see cref="TemplateFormat.Markdown"/>: Markdown-formatted narrative for documentation/display</item>
    ///   <item><see cref="TemplateFormat.Compact"/>: Single-line condensed format for AI/ML embeddings</item>
    /// </list>
    /// </returns>
    /// <remarks>
    /// <para>
    /// This API uses <see cref="IElement"/> instead of <see cref="Ignixa.Serialization.SourceNodes.ResourceJsonNode"/> to provide:
    /// </para>
    /// <list type="bullet">
    ///   <item>A cleaner abstraction that works with any source (JSON, XML, or in-memory)</item>
    ///   <item>Type-safe access to FHIR elements through the element tree</item>
    ///   <item>Consistency with internal template engine architecture</item>
    /// </list>
    /// <para>
    /// XSS sanitization is only applied for the Html format. Markdown and Compact formats
    /// return the raw template output without HTML sanitization.
    /// </para>
    /// <para>
    /// The FHIR version is determined by the <see cref="ISchema"/> provided during generator creation.
    /// This ensures version consistency and simplifies the API.
    /// </para>
    /// </remarks>
    Task<string> GenerateNarrativeAsync(
        IElement element,
        string resourceType,
        CultureInfo? culture = null,
        TemplateFormat format = TemplateFormat.Html,
        CancellationToken cancellationToken = default);
}
