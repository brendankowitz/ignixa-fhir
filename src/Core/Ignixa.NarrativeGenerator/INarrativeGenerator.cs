// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using Ignixa.Abstractions;

namespace Ignixa.NarrativeGenerator;

/// <summary>
/// Generates XHTML narrative for FHIR resources using Scriban templates.
/// </summary>
public interface INarrativeGenerator
{
    /// <summary>
    /// Generates a WCAG 2.1 AA compliant XHTML narrative for a FHIR resource.
    /// </summary>
    /// <param name="element">The FHIR resource element to generate narrative for. Must be created with an appropriate <see cref="ISchema"/> that matches the <paramref name="fhirVersion"/>.</param>
    /// <param name="resourceType">The FHIR resource type (e.g., "Patient", "Observation"). This is required because <see cref="IElement"/> doesn't carry type information.</param>
    /// <param name="fhirVersion">The FHIR version of the resource. Used to select version-appropriate narrative templates.</param>
    /// <param name="culture">The culture for localization (defaults to current culture).</param>
    /// <param name="cancellationToken">Cancellation token for async operations.</param>
    /// <returns>Sanitized XHTML narrative content.</returns>
    /// <remarks>
    /// This API uses <see cref="IElement"/> instead of <see cref="Ignixa.Serialization.SourceNodes.ResourceJsonNode"/> to provide:
    /// <list type="bullet">
    ///   <item>A cleaner abstraction that works with any source (JSON, XML, or in-memory)</item>
    ///   <item>Type-safe access to FHIR elements through the element tree</item>
    ///   <item>Consistency with internal template engine architecture</item>
    /// </list>
    /// </remarks>
    Task<string> GenerateNarrativeAsync(
        IElement element,
        string resourceType,
        FhirVersion fhirVersion,
        CultureInfo? culture = null,
        CancellationToken cancellationToken = default);
}
