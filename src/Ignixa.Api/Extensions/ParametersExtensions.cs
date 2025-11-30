// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Serialization;
using Ignixa.Serialization.Models;
using Ignixa.Serialization.SourceNodes;

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
            .Where(v => v != null)!;
    }

    /// <summary>
    /// Gets a resource parameter by name and casts to the specified type.
    /// </summary>
    public static T? GetParameterResource<T>(this ParametersJsonNode parameters, string name)
        where T : ResourceJsonNode
    {
        var param = parameters.Parameter?.FirstOrDefault(p => p.Name == name);
        if (param?.Resource == null)
        {
            return null;
        }

        // If T is ResourceJsonNode, return directly
        if (typeof(T) == typeof(ResourceJsonNode))
        {
            return (T)(object)param.Resource;
        }

        // Otherwise, re-parse as specific type
        var json = param.Resource.MutableNode.ToJsonString();
        return JsonSourceNodeFactory.Parse<T>(json);
    }

    /// <summary>
    /// Gets multiple resource parameters by name (for parameters that can repeat).
    /// </summary>
    public static IEnumerable<T> GetParameterResources<T>(this ParametersJsonNode parameters, string name)
        where T : ResourceJsonNode
    {
        return parameters.Parameter
            .Where(p => p.Name == name)
            .Select(p =>
            {
                if (p.Resource == null)
                {
                    return null;
                }

                // If T is ResourceJsonNode, return directly
                if (typeof(T) == typeof(ResourceJsonNode))
                {
                    return (T)(object)p.Resource;
                }

                // Otherwise, re-parse as specific type
                try
                {
                    var json = p.Resource.MutableNode.ToJsonString();
                    return JsonSourceNodeFactory.Parse<T>(json);
                }
                catch
                {
                    return null;
                }
            })
            .Where(r => r != null)!;
    }
}
