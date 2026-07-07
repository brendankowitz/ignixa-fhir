// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.PackageManagement.Infrastructure;
using Ignixa.PackageManagement.Models;
using Ignixa.Validation.Schema;
using Ignixa.Validation.Services;
using Microsoft.Extensions.Logging;

namespace Ignixa.PackageManagement.Validation;

/// <summary>
/// Product surface for constructing a package-backed validator: given a base FHIR schema and the
/// conformance resources of one or more loaded packages, composes the layered schema resolver and
/// terminology surface downstream profile/extension/terminology validation reads from.
/// <para>
/// With no packages this reduces to the base-schema resolver chain, so out-of-the-box behaviour is
/// unchanged. The wiring here mirrors the previously test-only <c>PackageValidatorFactory</c>,
/// promoted to a reusable, options-driven entry point.
/// </para>
/// </summary>
public static class PackageBackedValidator
{
    /// <summary>
    /// Composes a <see cref="PackageBackedValidationSetup"/> from the supplied options.
    /// </summary>
    /// <param name="options">Base schema, package resources, and layering flags.</param>
    /// <returns>A ready-to-use profile-aware resolver with its terminology and content providers.</returns>
    public static PackageBackedValidationSetup Create(PackageValidationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.BaseSchemaProvider);
        ArgumentNullException.ThrowIfNull(options.PackageResources);

        var baseProvider = options.BaseSchemaProvider;
        var loggerFactory = options.LoggerFactory;

        var schemaResources = SelectSchemaResources(options);
        var packageSchema = new ProfileLayeredSchemaProvider(
            baseProvider,
            schemaResources,
            loggerFactory?.CreateLogger<ProfileLayeredSchemaProvider>());

        var codeSystemSource = new PackageCodeSystemSource(
            options.PackageResources,
            loggerFactory?.CreateLogger<PackageCodeSystemSource>());

        var valueSetSource = new PackageValueSetSource(
            options.PackageResources,
            loggerFactory?.CreateLogger<PackageValueSetSource>());

        // Layer package value sets only when requested: the binding path then queries them before
        // the base provider. When not layered, binding validation stays identical to base-only —
        // useful for loading a core package purely to make extensions and CodeSystems resolve.
        var terminology = options.LayerPackageValueSets
            ? new InMemoryTerminologyService(
                primary: baseProvider.ValueSetProvider,
                additional: [valueSetSource],
                codeSystemProvider: codeSystemSource)
            : new InMemoryTerminologyService(
                baseProvider.ValueSetProvider,
                codeSystemProvider: codeSystemSource);

        var inner = new StructureDefinitionSchemaResolver(packageSchema, terminologyService: terminology);
        var cached = new CachedValidationSchemaResolver(inner);
        var resolver = new ProfileAwareValidationSchemaResolver(cached);

        return new PackageBackedValidationSetup(
            resolver,
            terminology,
            packageSchema,
            valueSetSource,
            codeSystemSource);
    }

    /// <summary>
    /// Returns the resources handed to the schema layer. When
    /// <see cref="PackageValidationOptions.ExcludeBaseTypeStructureDefinitions"/> is set, package
    /// <c>StructureDefinition</c>s whose id names a base-spec type are dropped so they do not shadow
    /// the generated base schema; non-StructureDefinition resources are always retained (the schema
    /// provider ignores them, but the snapshot base resolver may read them).
    /// </summary>
    private static IReadOnlyList<ExtractedResource> SelectSchemaResources(PackageValidationOptions options)
    {
        if (!options.ExcludeBaseTypeStructureDefinitions)
        {
            return options.PackageResources;
        }

        var baseProvider = options.BaseSchemaProvider;
        return options.PackageResources
            .Where(r => r.ResourceType != "StructureDefinition" || !baseProvider.IsKnownType(r.ResourceId))
            .ToList();
    }
}
