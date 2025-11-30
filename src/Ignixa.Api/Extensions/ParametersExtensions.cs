// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Nodes;
using Ignixa.Serialization;
using Ignixa.Serialization.Models;
using Ignixa.Serialization.SourceNodes;
using Microsoft.Extensions.Logging;

namespace Ignixa.Api.Extensions;

/// <summary>
/// Extension methods for extracting parameter values from FHIR Parameters resources.
/// </summary>
public static class ParametersExtensions
{
    /// <summary>
    /// Gets a single string parameter value by name.
    /// </summary>
    public static string? GetParameterStringValue(this ParametersJsonNode parameters, string name)
    {
        var param = parameters.FindParameter(name);
        return param?.GetValueAs<string>();
    }

    /// <summary>
    /// Gets multiple string parameter values by name (for parameters that can repeat).
    /// </summary>
    public static IEnumerable<string> GetParameterStringValues(this ParametersJsonNode parameters, string name)
    {
        return parameters.Parameter
            .Where(p => p.Name == name)
            .Select(p => p.GetValueAs<string>())
            .Where(v => v is not null)!;
    }

    /// <summary>
    /// Gets a resource parameter by name and casts to the specified type.
    /// </summary>
    /// <typeparam name="T">The resource type to deserialize to.</typeparam>
    /// <param name="parameters">The Parameters resource containing the parameter.</param>
    /// <param name="name">The name of the parameter to extract.</param>
    /// <param name="logger">Optional logger for warning on deserialization failures.</param>
    /// <returns>The deserialized resource, or null if not found or deserialization fails.</returns>
    public static T? GetParameterResource<T>(
        this ParametersJsonNode parameters,
        string name,
        ILogger? logger = null)
        where T : ResourceJsonNode
    {
        var param = parameters.Parameter?.FirstOrDefault(p => p.Name == name);
        if (param?.Resource is null)
        {
            return null;
        }

        // If T is ResourceJsonNode, return directly
        if (typeof(T) == typeof(ResourceJsonNode))
        {
            return (T)(object)param.Resource;
        }

        // Otherwise, re-parse as specific type using JsonNode overload (avoids serialization roundtrip)
        try
        {
            return JsonSourceNodeFactory.Parse<T>(param.Resource.MutableNode);
        }
        catch (JsonException ex)
        {
            logger?.LogWarning(
                ex,
                "Failed to deserialize parameter '{ParameterName}' resource to type {TypeName}. Returning null.",
                name,
                typeof(T).Name);
            return null;
        }
    }

    /// <summary>
    /// Gets multiple resource parameters by name (for parameters that can repeat).
    /// </summary>
    /// <typeparam name="T">The resource type to deserialize to.</typeparam>
    /// <param name="parameters">The Parameters resource containing the parameters.</param>
    /// <param name="name">The name of the parameters to extract.</param>
    /// <param name="logger">Optional logger for warning on deserialization failures.</param>
    /// <returns>An enumerable of deserialized resources. Invalid resources are skipped with a warning log.</returns>
    public static IEnumerable<T> GetParameterResources<T>(
        this ParametersJsonNode parameters,
        string name,
        ILogger? logger = null)
        where T : ResourceJsonNode
    {
        return parameters.Parameter
            .Where(p => p.Name == name)
            .Select(p =>
            {
                if (p.Resource is null)
                {
                    return null;
                }

                // If T is ResourceJsonNode, return directly
                if (typeof(T) == typeof(ResourceJsonNode))
                {
                    return (T)(object)p.Resource;
                }

                // Otherwise, re-parse as specific type using JsonNode overload (avoids serialization roundtrip)
                try
                {
                    return JsonSourceNodeFactory.Parse<T>(p.Resource.MutableNode);
                }
                catch (JsonException ex)
                {
                    logger?.LogWarning(
                        ex,
                        "Failed to deserialize parameter '{ParameterName}' resource to type {TypeName}. Skipping invalid resource.",
                        name,
                        typeof(T).Name);
                    return null;
                }
            })
            .Where(r => r is not null)!;
    }
}
