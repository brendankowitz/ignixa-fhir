// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.Serialization.Models;

namespace Ignixa.Serialization.Extensions;

/// <summary>
/// Version-agnostic extension methods for StructureMap operations.
/// Provides unified API while preserving version-specific fidelity.
/// </summary>
public static class StructureMapExtensions
{
    /// <summary>
    /// Gets dependent variables regardless of FHIR version.
    /// R4/R4B: Returns Variable string array.
    /// R5+: Extracts string values from Parameter array.
    /// </summary>
    /// <param name="dependent">The dependent node.</param>
    /// <returns>Enumerable of variable names.</returns>
    public static IEnumerable<string> GetDependentVariables(this StructureMapDependentJsonNode dependent)
    {
        ArgumentNullException.ThrowIfNull(dependent);

        if (!dependent.FhirVersion.HasValue)
        {
            // No version set - try both and return whichever works
            try
            {
                var variables = dependent.Variable;
                if (variables != null && variables.Any())
                {
                    return variables;
                }
            }
            catch (NotSupportedException)
            {
                // R5+ context
            }

            try
            {
                return dependent.Parameter
                    .Select(p => p.GetValueAs<string>())
                    .Where(v => v != null)!;
            }
            catch (NotSupportedException)
            {
                // R4 context
            }

            return Enumerable.Empty<string>();
        }

        // Version is set - use appropriate accessor
        return dependent.FhirVersion >= FhirVersion.R5
            ? dependent.Parameter.Select(p => p.GetValueAs<string>() ?? string.Empty)
            : dependent.Variable ?? Enumerable.Empty<string>();
    }

    /// <summary>
    /// Adds a dependent variable using version-appropriate format.
    /// R4/R4B: Adds to Variable string array.
    /// R5+: Creates StructureMapParameter with valueString.
    /// </summary>
    /// <param name="dependent">The dependent node.</param>
    /// <param name="variable">The variable name to add.</param>
    /// <exception cref="InvalidOperationException">Thrown when FhirVersion is not set.</exception>
    public static void AddDependentVariable(this StructureMapDependentJsonNode dependent, string variable)
    {
        ArgumentNullException.ThrowIfNull(dependent);
        ArgumentNullException.ThrowIfNull(variable);

        if (!dependent.FhirVersion.HasValue)
        {
            throw new InvalidOperationException(
                "FhirVersion must be set on StructureMapDependentJsonNode before adding variables. " +
                "Set FhirVersion on the parent StructureMapJsonNode before accessing children.");
        }

        if (dependent.FhirVersion >= FhirVersion.R5)
        {
            var param = new StructureMapParameterJsonNode(new JsonObject(), dependent.FhirVersion);
            param.SetValue("String", JsonValue.Create(variable));
            dependent.Parameter.Add(param);
        }
        else
        {
            dependent.Variable.Add(variable);
        }
    }

    /// <summary>
    /// Gets default value as string regardless of version.
    /// R4/R4B: Converts from defaultValue[x] (best effort).
    /// R5: Returns defaultValue string directly.
    /// </summary>
    /// <param name="source">The source node.</param>
    /// <returns>The default value as a string, or null if not set.</returns>
    public static string? GetDefaultValueString(this StructureMapSourceJsonNode source)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (!source.FhirVersion.HasValue)
        {
            // No version set - try both approaches
            try
            {
                return source.DefaultValue;
            }
            catch (NotSupportedException)
            {
                // R4 context - try GetDefaultValue
                var node = source.GetDefaultValue();
                if (node is JsonValue jsonValue)
                {
                    return jsonValue.GetValue<string>();
                }
                return node?.ToString();
            }
        }

        if (source.FhirVersion >= FhirVersion.R5)
        {
            return source.DefaultValue;
        }
        else
        {
            // R4/R4B: Try to convert from defaultValue[x]
            var node = source.GetDefaultValue();
            if (node is JsonValue jsonValue)
            {
                // Try to get primitive value
                try
                {
                    return jsonValue.GetValue<string>();
                }
                catch
                {
                    // Not a string, convert to string representation
                    return jsonValue.ToString();
                }
            }
            return node?.ToString();
        }
    }

    /// <summary>
    /// Sets default value as string using version-appropriate format.
    /// R4/R4B: Creates defaultValueString.
    /// R5: Sets defaultValue string directly.
    /// </summary>
    /// <param name="source">The source node.</param>
    /// <param name="value">The default value string.</param>
    /// <exception cref="InvalidOperationException">Thrown when FhirVersion is not set.</exception>
    public static void SetDefaultValueString(this StructureMapSourceJsonNode source, string? value)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (!source.FhirVersion.HasValue)
        {
            throw new InvalidOperationException(
                "FhirVersion must be set on StructureMapSourceJsonNode before setting default values. " +
                "Set FhirVersion on the parent StructureMapJsonNode before accessing children.");
        }

        if (source.FhirVersion >= FhirVersion.R5)
        {
            source.DefaultValue = value;
        }
        else
        {
            source.SetDefaultValue("String", value != null ? JsonValue.Create(value) : null);
        }
    }

    /// <summary>
    /// Checks if constants are supported for this StructureMap's FHIR version.
    /// </summary>
    /// <param name="structureMap">The structure map.</param>
    /// <returns>True if R5+, false otherwise.</returns>
    public static bool SupportsConstants(this StructureMapJsonNode structureMap)
    {
        ArgumentNullException.ThrowIfNull(structureMap);
        return structureMap.FhirVersion.HasValue && structureMap.FhirVersion >= FhirVersion.R5;
    }

    /// <summary>
    /// Safely gets constants if supported, empty list otherwise.
    /// Prevents NotSupportedException when checking for constants in R4 context.
    /// </summary>
    /// <param name="structureMap">The structure map.</param>
    /// <returns>List of constants for R5+, empty list for R4/R4B.</returns>
    public static IEnumerable<StructureMapConstJsonNode> GetConstantsOrEmpty(this StructureMapJsonNode structureMap)
    {
        ArgumentNullException.ThrowIfNull(structureMap);

        if (!structureMap.SupportsConstants())
        {
            return Enumerable.Empty<StructureMapConstJsonNode>();
        }

        return structureMap.Const;
    }
}
