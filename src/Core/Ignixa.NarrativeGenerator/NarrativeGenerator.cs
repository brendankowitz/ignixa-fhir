// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using Ignixa.Abstractions;
using Ignixa.NarrativeGenerator.Engine;
using Ignixa.NarrativeGenerator.Security;

namespace Ignixa.NarrativeGenerator;

/// <summary>
/// Orchestrates the complete narrative generation pipeline for FHIR resources.
/// </summary>
/// <remarks>
/// <para>
/// This class coordinates three main components to generate safe, localized XHTML narratives:
/// </para>
/// <list type="number">
///   <item><see cref="ITemplateResolver"/> - Resolves version-appropriate Scriban templates</item>
///   <item><see cref="NarrativeTemplateEngine"/> - Renders templates with resource context</item>
///   <item><see cref="XhtmlSanitizer"/> - Sanitizes output to prevent XSS attacks</item>
/// </list>
/// <para>
/// Thread-safety: This class is thread-safe and can be registered as a singleton.
/// </para>
/// </remarks>
public class FhirNarrativeGenerator(
    ITemplateResolver templateResolver,
    NarrativeTemplateEngine templateEngine,
    XhtmlSanitizer sanitizer) : INarrativeGenerator
{
    /// <inheritdoc />
    public async Task<string> GenerateNarrativeAsync(
        IElement element,
        string resourceType,
        FhirVersion fhirVersion,
        CultureInfo? culture = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentException.ThrowIfNullOrEmpty(resourceType);

        var actualCulture = culture ?? CultureInfo.CurrentCulture;

        // 1. Resolve template (version-specific → Normative → Generic fallback)
        var resolution = await templateResolver.ResolveTemplateAsync(resourceType, fhirVersion, cancellationToken);

        if (resolution is null)
        {
            throw new InvalidOperationException(
                $"No template found for resource type '{resourceType}' (FHIR version: {fhirVersion})");
        }

        // 2. Render template with element (already IElement - no conversion needed)
        var rendered = await templateEngine.RenderAsync(
            resolution.Content,
            element,
            resourceType,
            fhirVersion,
            actualCulture,
            cancellationToken);

        // 3. Sanitize output for XSS protection
        var sanitized = sanitizer.Sanitize(rendered);

        return sanitized;
    }
}
