// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.FhirMappingLanguage.Expressions;

namespace Ignixa.CliOnFhir.Mapping;

/// <summary>
/// Where a run enters a map: its first group, which takes one resource as its source and builds one resource as its target. A
/// parameter's type is the type it names, or the url of the <c>uses</c> statement whose alias it names, cut to its last segment;
/// a parameter without one takes the type of the map's first <c>uses ... as source</c> (or <c>as target</c>).
/// </summary>
/// <param name="Group">The first group's name, as the map spells it.</param>
/// <param name="SourceName">The source parameter.</param>
/// <param name="SourceType">The resource type it takes, or null when the map does not say (then every resource in scope is given to it).</param>
/// <param name="TargetName">The target parameter.</param>
/// <param name="TargetType">The resource type it builds.</param>
internal sealed record MapEntry(string Group, string SourceName, string? SourceType, string TargetName, string TargetType)
{
    /// <exception cref="ToolRequestException">
    /// <c>map-unsupported</c>: the first group does not take exactly one source and one target, or its target is not a resource type
    /// (or its source is named and is not one).
    /// </exception>
    public static MapEntry Of(MapExpression map, IFhirSchemaProvider schema)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(schema);

        GroupExpression group = map.Groups[0];
        List<ParameterExpression> sources = [.. group.Parameters.Where(parameter => parameter.Mode == ParameterMode.Source)];
        List<ParameterExpression> targets = [.. group.Parameters.Where(parameter => parameter.Mode == ParameterMode.Target)];
        if (sources.Count != 1 || targets.Count != 1)
        {
            throw ToolRequestException.Error("map-unsupported", $"The map's first group takes {sources.Count} source and {targets.Count} target parameters; map runs one resource of the folder through a group that takes one source and builds one target resource.");
        }

        string? sourceType = TypeOf(map, sources[0].Type, ModelMode.Source);
        string? targetType = TypeOf(map, targets[0].Type, ModelMode.Target);
        if (sourceType is not null && !schema.ResourceTypeNames.Contains(sourceType))
        {
            throw ToolRequestException.Error("map-unsupported", $"The map's source is not a {schema.Version} resource type, so no resource of the folder can be its input.");
        }

        if (targetType is null || !schema.ResourceTypeNames.Contains(targetType))
        {
            throw ToolRequestException.Error("map-unsupported", $"The map's target is {(targetType is null ? "of no type it names" : $"not a {schema.Version} resource type")}; map builds resources, one NDJSON file per type.");
        }

        return new MapEntry(group.Name, sources[0].Name, sourceType, targets[0].Name, targetType);
    }

    private static string? TypeOf(MapExpression map, string? type, ModelMode mode)
    {
        UsesExpression? uses = type is null
            ? map.Uses.FirstOrDefault(candidate => candidate.Mode == mode)
            : map.Uses.FirstOrDefault(candidate => string.Equals(candidate.Alias, type, StringComparison.Ordinal));
        string? named = uses?.Url ?? type;
        return named is null ? null : named[(named.LastIndexOf('/') + 1)..];
    }
}
