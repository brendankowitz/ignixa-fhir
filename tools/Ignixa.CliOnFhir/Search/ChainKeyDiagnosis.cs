// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.Search.Definition;
using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.CliOnFhir.Search;

/// <summary>
/// Says which link of a chained (<c>subject:Patient.name</c>) or reverse-chained (<c>_has:Observation:subject:code</c>)
/// key the search pipeline could not bind. The pipeline reports every such failure as "the chained parameter is not
/// supported", which reads as if chains were unsupported; this walks the key against the definitions instead.
/// </summary>
internal sealed class ChainKeyDiagnosis(IFhirSchemaProvider schema, ISearchParameterDefinitionManager definitions, Func<string, IReadOnlyList<string>> knownNames)
{
    /// <summary>
    /// The failure for the first link of <paramref name="key"/> that cannot bind on <paramref name="resourceType"/>:
    /// <c>unknown-search-parameter</c> (with a hint) for a name the type does not define, <c>invalid-search-request</c>
    /// for a link that can never resolve. Null when every link binds, so the caller keeps the pipeline's own account.
    /// </summary>
    public ToolRequestException? Diagnose(string resourceType, string key, string value) =>
        Walk([resourceType], key, $"{key}={value}");

    private ToolRequestException? Walk(IReadOnlyList<string> types, string key, string parameter)
    {
        if (key.StartsWith("_has:", StringComparison.Ordinal))
        {
            return Reverse(types, key, parameter);
        }

        int dot = key.IndexOf('.', StringComparison.Ordinal);
        string link = dot < 0 ? key : key[..dot];
        int colon = link.IndexOf(':', StringComparison.Ordinal);
        string name = colon < 0 ? link : link[..colon];
        if (types.FirstOrDefault(type => !definitions.TryGetSearchParameter(type, name, out _)) is { } missing)
        {
            return Unknown(parameter, name, missing);
        }

        if (dot < 0)
        {
            return null;
        }

        definitions.TryGetSearchParameter(types[0], name, out SearchParameterInfo reference);
        if (reference.Type != SearchParamType.Reference)
        {
            return Invalid(parameter, $"'{name}' is a {reference.Type} parameter of {types[0]}, so it cannot be chained; only reference parameters can.");
        }

        string? typed = colon < 0 ? null : link[(colon + 1)..];
        if (typed is not null && !reference.TargetResourceTypes.Contains(typed, StringComparer.Ordinal))
        {
            return Invalid(parameter, $"{types[0]}.{name} never references {typed}; it references {string.Join(", ", reference.TargetResourceTypes)}.");
        }

        string rest = key[(dot + 1)..];
        string nextName = ParameterPolicy.BaseName(rest);
        IReadOnlyList<string> targets = typed is null
            ? [.. reference.TargetResourceTypes.Where(target => definitions.TryGetSearchParameter(target, nextName, out _))]
            : [typed];
        if (targets.Count == 0)
        {
            return Unknown(parameter, nextName, reference.TargetResourceTypes);
        }

        return targets.Count == 1 ? Walk(targets, rest, parameter) : null;
    }

    private ToolRequestException? Reverse(IReadOnlyList<string> types, string key, string parameter)
    {
        string[] parts = key.Split(':', 4);
        if (parts.Length < 4 || parts[3].Length == 0)
        {
            return Invalid(parameter, "a reverse chain is written _has:Type:reference:parameter, for example _has:Observation:subject:code.");
        }

        (string source, string referenceName, string rest) = (parts[1], parts[2], parts[3]);
        if (!schema.ResourceTypeNames.Contains(source))
        {
            return Invalid(parameter, $"'{source}' is not a {schema.Version} resource type.");
        }

        if (!definitions.TryGetSearchParameter(source, referenceName, out SearchParameterInfo reference))
        {
            return Unknown(parameter, referenceName, source);
        }

        if (reference.Type != SearchParamType.Reference)
        {
            return Invalid(parameter, $"'{referenceName}' is a {reference.Type} parameter of {source}; _has needs a reference parameter.");
        }

        if (!reference.TargetResourceTypes.Intersect(types, StringComparer.Ordinal).Any())
        {
            return Invalid(parameter, $"{source}.{referenceName} never references {string.Join(" or ", types)} (it references {string.Join(", ", reference.TargetResourceTypes)}), so no resource can match.");
        }

        return Walk([source], rest, parameter);
    }

    private ToolRequestException Unknown(string parameter, string name, string resourceType) =>
        ToolRequestException.Error(
            SearchQueryParser.UnknownParameterCode,
            $"'{parameter}': '{name}' is not a search parameter of {resourceType}. {ParameterSuggestions.For(name, resourceType, knownNames(resourceType))}{SearchQueryParser.LenientHint}");

    private ToolRequestException Unknown(string parameter, string name, IReadOnlyList<string> resourceTypes) =>
        resourceTypes.Count == 1
            ? Unknown(parameter, name, resourceTypes[0])
            : ToolRequestException.Error(
                SearchQueryParser.UnknownParameterCode,
                $"'{parameter}': '{name}' is not a search parameter of any type the reference can point at ({string.Join(", ", resourceTypes)}). Name the type, for example ':{resourceTypes[0]}', to get suggestions.{SearchQueryParser.LenientHint}");

    private static ToolRequestException Invalid(string parameter, string reason) =>
        ToolRequestException.Error(SearchFailures.InvalidRequestCode, $"'{parameter}': {reason}");
}
