// <copyright file="StructureDefinitionSchemaResolver.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using Ignixa.Abstractions;
using Ignixa.Specification;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Services;

namespace Ignixa.Validation.Schema;

/// <summary>
/// Resolves validation schemas by using ISchema and StructureDefinitionSchemaBuilder
/// to build ValidationSchema objects on-demand from FHIR StructureDefinition metadata.
/// </summary>
public class StructureDefinitionSchemaResolver : IValidationSchemaResolver
{
    private readonly ISchema _schema;
    private readonly StructureDefinitionSchemaBuilder _builder;
    private readonly IReadOnlySet<string>? _validResourceTypes;
    private readonly ITerminologyService? _terminologyService;

    /// <summary>
    /// Initializes a new instance of the <see cref="StructureDefinitionSchemaResolver"/> class.
    /// </summary>
    /// <param name="schema">The schema provider.</param>
    /// <param name="builder">The schema builder (optional, creates default if null).</param>
    /// <param name="terminologyService">Optional terminology service for binding validation. If null, binding checks are not created.</param>
    /// <exception cref="ArgumentNullException">Thrown if schema is null.</exception>
    public StructureDefinitionSchemaResolver(
        ISchema schema,
        StructureDefinitionSchemaBuilder? builder = null,
        ITerminologyService? terminologyService = null)
    {
        _schema = schema ?? throw new ArgumentNullException(nameof(schema));
        _builder = builder ?? new StructureDefinitionSchemaBuilder();

        // If no terminology service provided, create default InMemoryTerminologyService
        // using the schema's ValueSetProvider if available
        _terminologyService = terminologyService ??
            (schema is IFhirSchemaProvider schemaProvider
                ? new InMemoryTerminologyService(schemaProvider.ValueSetProvider)
                : throw new InvalidOperationException("Schema must implement IFhirSchemaProvider to use default InMemoryTerminologyService"));

        // Extract valid resource types if schema is an IFhirSchemaProvider
        _validResourceTypes = (schema as IFhirSchemaProvider)?.ResourceTypeNames;
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

        // Get type definition from schema
        var typeDefinition = _schema.GetTypeDefinition(resourceType);
        if (typeDefinition == null)
        {
            return null;
        }

        // Build schema using builder, passing terminology service for binding validation and valid resource types
        return _builder.BuildSchema(typeDefinition, _schema, terminologyService: _terminologyService, validResourceTypes: _validResourceTypes);
    }

    /// <summary>
    /// Extracts the resource type from a canonical URL or returns the input if it's already a resource type name.
    /// </summary>
    /// <param name="canonicalUrlOrTypeName">
    /// Either a canonical URL (e.g., "http://hl7.org/fhir/StructureDefinition/Patient")
    /// or just the resource type name (e.g., "Patient").
    /// </param>
    /// <returns>The resource type name, or null if extraction fails.</returns>
    private static string? ExtractResourceType(string canonicalUrlOrTypeName)
    {
        // If no slash, assume it's already a resource type name
        var lastSlashIndex = canonicalUrlOrTypeName.LastIndexOf('/');
        if (lastSlashIndex < 0)
        {
            return canonicalUrlOrTypeName;
        }

        // Extract last segment from URL
        if (lastSlashIndex == canonicalUrlOrTypeName.Length - 1)
        {
            return null;
        }

        return canonicalUrlOrTypeName.Substring(lastSlashIndex + 1);
    }
}
