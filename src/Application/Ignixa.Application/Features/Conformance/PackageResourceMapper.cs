// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using Ignixa.Domain.Models;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Application.Features.Conformance;

/// <summary>
/// Maps PackageResource entities to DTOs for activation pipeline.
/// </summary>
public static class PackageResourceMapper
{
    /// <summary>
    /// Converts PackageResource entities to PackageResources DTO.
    /// </summary>
    public static PackageResources MapToPackageResources(PackageResource[] resources)
    {
        var searchParameters = new List<SearchParameterInfo>();
        var structureDefinitions = new List<StructureDefinitionInfo>();

        foreach (var resource in resources)
        {
            switch (resource.ResourceType)
            {
                case "SearchParameter":
                    var sp = MapSearchParameter(resource);
                    if (sp != null)
                    {
                        searchParameters.Add(sp);
                    }
                    break;

                case "StructureDefinition":
                    var sd = MapStructureDefinition(resource);
                    if (sd != null)
                    {
                        structureDefinitions.Add(sd);
                    }
                    break;
            }
        }

        return new PackageResources(searchParameters, structureDefinitions);
    }

    private static SearchParameterInfo? MapSearchParameter(PackageResource resource)
    {
        try
        {
            using var doc = JsonDocument.Parse(resource.ResourceJson);
            var root = doc.RootElement;

            var code = root.GetProperty("code").GetString();
            var expression = root.GetProperty("expression").GetString();
            var typeStr = root.GetProperty("type").GetString();

            if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(expression) || string.IsNullOrEmpty(typeStr))
            {
                return null;
            }

            var type = ParseSearchParamType(typeStr);
            var baseTypes = new List<string>();

            if (root.TryGetProperty("base", out var baseArray))
            {
                foreach (var baseType in baseArray.EnumerateArray())
                {
                    var bt = baseType.GetString();
                    if (!string.IsNullOrEmpty(bt))
                    {
                        baseTypes.Add(bt);
                    }
                }
            }

            string? derivedFrom = null;
            if (root.TryGetProperty("derivedFrom", out var derivedFromProp))
            {
                derivedFrom = derivedFromProp.GetString();
            }

            List<CompositeComponent>? components = null;
            if (type == SearchParamType.Composite && root.TryGetProperty("component", out var componentArray))
            {
                components = [];
                foreach (var comp in componentArray.EnumerateArray())
                {
                    var defUrl = comp.GetProperty("definition").GetString();
                    var expr = comp.GetProperty("expression").GetString();
                    if (!string.IsNullOrEmpty(defUrl) && !string.IsNullOrEmpty(expr))
                    {
                        components.Add(new CompositeComponent(defUrl, expr));
                    }
                }
            }

            List<string>? targetResourceTypes = null;
            if (type == SearchParamType.Reference && root.TryGetProperty("target", out var targetArray))
            {
                targetResourceTypes = [];
                foreach (var target in targetArray.EnumerateArray())
                {
                    var targetType = target.GetString();
                    if (!string.IsNullOrEmpty(targetType))
                    {
                        targetResourceTypes.Add(targetType);
                    }
                }
            }

            string? name = null;
            if (root.TryGetProperty("name", out var nameProp))
            {
                name = nameProp.GetString();
            }

            string? description = null;
            if (root.TryGetProperty("description", out var descProp))
            {
                description = descProp.GetString();
            }

            return new SearchParameterInfo(
                resource.Canonical,
                code,
                baseTypes,
                expression,
                type,
                derivedFrom,
                resource.PackageId,
                components,
                targetResourceTypes,
                name,
                description);
        }
        catch
        {
            return null;
        }
    }

    private static StructureDefinitionInfo? MapStructureDefinition(PackageResource resource)
    {
        try
        {
            using var doc = JsonDocument.Parse(resource.ResourceJson);
            var root = doc.RootElement;

            var type = root.GetProperty("type").GetString();
            var kind = root.GetProperty("kind").GetString();

            if (string.IsNullOrEmpty(type) || string.IsNullOrEmpty(kind))
            {
                return null;
            }

            // Extract snapshot JSON if present
            string snapshotJson = "{}";
            if (root.TryGetProperty("snapshot", out var snapshot))
            {
                snapshotJson = snapshot.GetRawText();
            }

            return new StructureDefinitionInfo(
                resource.Canonical,
                type,
                kind,
                snapshotJson);
        }
        catch
        {
            return null;
        }
    }

    private static SearchParamType ParseSearchParamType(string type)
    {
        return type.ToUpperInvariant() switch
        {
            "COMPOSITE" => SearchParamType.Composite,
            "DATE" => SearchParamType.Date,
            "NUMBER" => SearchParamType.Number,
            "QUANTITY" => SearchParamType.Quantity,
            "REFERENCE" => SearchParamType.Reference,
            "SPECIAL" => SearchParamType.Special,
            "STRING" => SearchParamType.String,
            "TOKEN" => SearchParamType.Token,
            "URI" => SearchParamType.Uri,
            _ => SearchParamType.String,
        };
    }
}
