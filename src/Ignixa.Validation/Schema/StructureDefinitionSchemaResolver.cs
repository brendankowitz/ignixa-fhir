// <copyright file="StructureDefinitionSchemaResolver.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using Ignixa.Abstractions;
using Ignixa.Validation.Abstractions;

namespace Ignixa.Validation.Schema;

/// <summary>
/// Resolves validation schemas by using IStructureDefinitionSummaryProvider and StructureDefinitionSchemaBuilder
/// to build ValidationSchema objects on-demand from FHIR StructureDefinition metadata.
/// </summary>
public class StructureDefinitionSchemaResolver : IValidationSchemaResolver
{
    private readonly IStructureDefinitionSummaryProvider _provider;
    private readonly StructureDefinitionSchemaBuilder _builder;

    /// <summary>
    /// Initializes a new instance of the <see cref="StructureDefinitionSchemaResolver"/> class.
    /// </summary>
    /// <param name="provider">The structure definition summary provider.</param>
    /// <param name="builder">The schema builder (optional, creates default if null).</param>
    /// <exception cref="ArgumentNullException">Thrown if provider is null.</exception>
    public StructureDefinitionSchemaResolver(
        IStructureDefinitionSummaryProvider provider,
        StructureDefinitionSchemaBuilder? builder = null)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _builder = builder ?? new StructureDefinitionSchemaBuilder();
    }

    /// <summary>
    /// Gets the validation schema for a given canonical URL (e.g., StructureDefinition URL).
    /// </summary>
    /// <param name="canonicalUrl">The canonical URL of the schema to retrieve.</param>
    /// <returns>The validation schema, or null if not found.</returns>
    public ValidationSchema? GetSchema(string canonicalUrl)
    {
        if (string.IsNullOrEmpty(canonicalUrl))
        {
            return null;
        }

        // Extract resource type from canonical URL
        // Format: http://hl7.org/fhir/StructureDefinition/{ResourceType}
        var resourceType = ExtractResourceType(canonicalUrl);
        if (string.IsNullOrEmpty(resourceType))
        {
            return null;
        }

        // Get summary from provider
        var summary = _provider.Provide(resourceType);
        if (summary == null)
        {
            return null;
        }

        // Build schema using builder
        return _builder.BuildSchema(summary, _provider);
    }

    /// <summary>
    /// Extracts the resource type from a canonical URL.
    /// </summary>
    /// <param name="canonicalUrl">The canonical URL (e.g., "http://hl7.org/fhir/StructureDefinition/Patient").</param>
    /// <returns>The resource type (last segment of the URL), or null if extraction fails.</returns>
    private static string? ExtractResourceType(string canonicalUrl)
    {
        // Extract last segment from URL
        var lastSlashIndex = canonicalUrl.LastIndexOf('/');
        if (lastSlashIndex < 0 || lastSlashIndex == canonicalUrl.Length - 1)
        {
            return null;
        }

        return canonicalUrl.Substring(lastSlashIndex + 1);
    }
}
