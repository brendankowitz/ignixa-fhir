// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.PackageManagement.Models;
using Ignixa.PackageManagement.Validation;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Generated;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Schema;

namespace Ignixa.Validation.Tests.TestHelpers;

/// <summary>
/// Validates a resource given as JSON through the public pipeline: an R4 element, the schema a profile-aware resolver composes
/// for it (core, plus each loaded <c>meta.profile</c>), and the two-argument <see cref="ValidationSchema.Validate(Ignixa.Abstractions.IElement, ValidationSettings)"/>.
/// </summary>
internal static class JsonResourceValidator
{
    private static readonly Lazy<ProfileAwareValidationSchemaResolver> Core = new(() =>
        new ProfileAwareValidationSchemaResolver(new CachedValidationSchemaResolver(new StructureDefinitionSchemaResolver(TestSchemaProvider.GetR4Schema()))));

    /// <summary>Validates <paramref name="json"/> at <paramref name="depth"/> against the core definitions.</summary>
    public static ValidationResult Validate(string json, ValidationDepth depth) =>
        Validate(json, new ValidationSettings { Depth = depth }, Core.Value);

    /// <summary>Validates <paramref name="json"/> with <paramref name="settings"/> against <paramref name="resolver"/>'s schemas.</summary>
    public static ValidationResult Validate(string json, ValidationSettings settings, ProfileAwareValidationSchemaResolver resolver)
    {
        var element = JsonNodeSourceNode.Create(JsonNode.Parse(json)!).ToElement(TestSchemaProvider.GetR4Schema());
        var schema = resolver.ResolveForElement(element) ?? throw new InvalidOperationException("The resource has no schema.");
        return schema.Validate(element, settings);
    }

    /// <summary>A resolver over the R4 core definitions with <paramref name="structureDefinitions"/> layered on them.</summary>
    public static ProfileAwareValidationSchemaResolver WithDefinitions(params string[] structureDefinitions)
    {
        var resources = structureDefinitions.Select(json =>
        {
            var root = JsonNode.Parse(json)!.AsObject();
            return new ExtractedResource
            {
                ResourceType = "StructureDefinition",
                Canonical = (string)root["url"]!,
                Version = (string?)root["version"],
                ResourceId = (string)root["id"]!,
                ResourceJson = json,
                FhirVersion = "4.0.1",
            };
        }).ToList();

        return PackageBackedValidator.Create(new PackageValidationOptions { BaseSchemaProvider = new R4CoreSchemaProvider(), PackageResources = resources }).SchemaResolver;
    }
}
