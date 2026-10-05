// <copyright file="StructureDefinitionSchemaResolver.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using System.Collections.Concurrent;
using Ignixa.Abstractions;
using Ignixa.Specification;
using Ignixa.Specification.Extensions;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Services;

namespace Ignixa.Validation.Schema;

/// <summary>
/// Resolves validation schemas by using ISchema and StructureDefinitionSchemaBuilder
/// to build ValidationSchema objects on-demand from FHIR StructureDefinition metadata.
/// </summary>
public class StructureDefinitionSchemaResolver : IValidationSchemaResolver
{
    private const string CoreCanonicalPrefix = "http://hl7.org/fhir/StructureDefinition/";
    private static readonly ConcurrentDictionary<FhirVersion, Lazy<IFhirSchemaProvider>> CoreSchemas = new();
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

        // Installed definitions are keyed by full canonical identity, including business version.
        var typeDefinition = _schema.GetTypeDefinition(canonicalUrl);
        if (typeDefinition == null && _schema is IFhirSchemaProvider provider &&
            canonicalUrl.StartsWith(CoreCanonicalPrefix, StringComparison.Ordinal))
        {
            string typeName = canonicalUrl[CoreCanonicalPrefix.Length..];
            int pipe = typeName.IndexOf('|', StringComparison.Ordinal);
            if (pipe >= 0)
            {
                if (typeName[(pipe + 1)..] != provider.FullVersion)
                {
                    return null;
                }
                typeName = typeName[..pipe];
            }
            if (typeName.Length > 0 && !typeName.Contains('/', StringComparison.Ordinal))
            {
                // Layered providers expose legacy profile-id aliases for simple names. Those are
                // not evidence that an unavailable core canonical identifies a core definition.
                var coreSchema = CoreSchemas.GetOrAdd(provider.Version,
                    static version => new Lazy<IFhirSchemaProvider>(() => version.GetSchemaProvider())).Value;
                typeDefinition = coreSchema.GetTypeDefinition(typeName);
            }
        }
        if (typeDefinition == null)
        {
            return null;
        }

        // Build schema using builder, passing terminology service for binding validation, valid resource types, and this resolver for contained resources
        return _builder.BuildSchema(typeDefinition, _schema, terminologyService: _terminologyService,
            validResourceTypes: _validResourceTypes, validationSchemaResolver: this,
            canonicalUrl: canonicalUrl.Contains(':', StringComparison.Ordinal) ? canonicalUrl : null);
    }
}
