// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;

namespace Ignixa.NarrativeGenerator.Engine;

/// <summary>
/// Resolves Scriban templates for FHIR resource types based on FHIR version and resource type.
/// </summary>
/// <remarks>
/// Template resolution follows this priority order:
/// <list type="number">
///   <item>Version-specific template (e.g., R4/Patient.scriban)</item>
///   <item>Normative template (e.g., Normative/Patient.scriban)</item>
///   <item>Version-specific generic template (e.g., R4/Generic.scriban)</item>
///   <item>Normative generic template (Normative/Generic.scriban) as final fallback</item>
/// </list>
/// </remarks>
public interface ITemplateResolver
{
    /// <summary>
    /// Resolves the best matching template for a given FHIR resource type and version.
    /// </summary>
    /// <param name="resourceType">The FHIR resource type (e.g., "Patient", "Observation").</param>
    /// <param name="fhirVersion">The FHIR version to target (R4, R4B, R5).</param>
    /// <param name="cancellationToken">Cancellation token for async operations.</param>
    /// <returns>
    /// A <see cref="TemplateResolution"/> containing the resolved template content and metadata,
    /// or null if no template could be found.
    /// </returns>
    Task<TemplateResolution?> ResolveTemplateAsync(
        string resourceType,
        FhirVersion fhirVersion,
        CancellationToken cancellationToken);

    /// <summary>
    /// Checks whether a template exists for the specified resource type and version.
    /// </summary>
    /// <param name="resourceType">The FHIR resource type.</param>
    /// <param name="fhirVersion">The FHIR version.</param>
    /// <returns>True if a template exists (including fallback templates), false otherwise.</returns>
    bool HasTemplate(string resourceType, FhirVersion fhirVersion);
}

/// <summary>
/// Represents the result of template resolution, including the template content and metadata.
/// </summary>
/// <param name="Content">The Scriban template content as a string.</param>
/// <param name="TemplatePath">The logical path of the resolved template (e.g., "R4/Patient.scriban").</param>
/// <param name="ResourceType">The resource type this template is designed for (or "Generic" for fallback).</param>
/// <param name="FhirVersion">The FHIR version folder from which the template was resolved.</param>
/// <param name="IsGenericFallback">True if this is a generic fallback template, false if resource-specific.</param>
public record TemplateResolution(
    string Content,
    string TemplatePath,
    string ResourceType,
    FhirVersion? FhirVersion,
    bool IsGenericFallback);
