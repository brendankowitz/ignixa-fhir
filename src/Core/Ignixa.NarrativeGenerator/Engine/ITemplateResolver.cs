// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;

namespace Ignixa.NarrativeGenerator.Engine;

/// <summary>
/// Resolves Scriban templates for FHIR resource types based on FHIR version, resource type, and output format.
/// </summary>
/// <remarks>
/// <para>
/// Templates are organized by format (Html, Md, Compact) and then by FHIR version.
/// </para>
/// <para>
/// Template resolution follows this priority order for each format:
/// </para>
/// <list type="number">
///   <item>Format-specific version template (e.g., Html/R4/Patient.scriban)</item>
///   <item>Format-specific normative template (e.g., Html/Patient.scriban)</item>
///   <item>Format-specific version generic template (e.g., Html/R4/Generic.scriban)</item>
///   <item>Format-specific normative generic template (e.g., Html/Generic.scriban) as final fallback</item>
/// </list>
/// </remarks>
internal interface ITemplateResolver
{
    /// <summary>
    /// Resolves the best matching template for a given FHIR resource type, version, and output format.
    /// </summary>
    /// <param name="resourceType">The FHIR resource type (e.g., "Patient", "Observation").</param>
    /// <param name="fhirVersion">The FHIR version to target (R4, R4B, R5).</param>
    /// <param name="format">The output format (Html, Markdown, or Compact).</param>
    /// <param name="cancellationToken">Cancellation token for async operations.</param>
    /// <returns>
    /// A <see cref="TemplateResolution"/> containing the resolved template content and metadata,
    /// or null if no template could be found.
    /// </returns>
    Task<TemplateResolution?> ResolveTemplateAsync(
        string resourceType,
        FhirVersion fhirVersion,
        TemplateFormat format,
        CancellationToken cancellationToken);

    /// <summary>
    /// Checks whether a template exists for the specified resource type, version, and format.
    /// </summary>
    /// <param name="resourceType">The FHIR resource type.</param>
    /// <param name="fhirVersion">The FHIR version.</param>
    /// <param name="format">The output format.</param>
    /// <returns>True if a template exists (including fallback templates), false otherwise.</returns>
    bool HasTemplate(string resourceType, FhirVersion fhirVersion, TemplateFormat format);
}

/// <summary>
/// Represents the result of template resolution, including the template content and metadata.
/// </summary>
/// <param name="Content">The Scriban template content as a string.</param>
/// <param name="TemplatePath">The logical path of the resolved template (e.g., "Html/R4/Patient.scriban").</param>
/// <param name="ResourceType">The resource type this template is designed for (or "Generic" for fallback).</param>
/// <param name="FhirVersion">The FHIR version folder from which the template was resolved.</param>
/// <param name="Format">The output format of the resolved template.</param>
/// <param name="IsGenericFallback">True if this is a generic fallback template, false if resource-specific.</param>
internal record TemplateResolution(
    string Content,
    string TemplatePath,
    string ResourceType,
    FhirVersion? FhirVersion,
    TemplateFormat Format,
    bool IsGenericFallback);
