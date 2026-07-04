// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace Ignixa.FhirFakes.Scenarios;

/// <summary>
/// Shared reflection-based parameter discovery and override-coercion logic used by both
/// <see cref="ScenarioCatalog"/> and <c>WorkflowScenarioCatalog</c>, so the two catalogs share one
/// tested implementation of override coercion, Min/Max validation, and default-value fallback
/// instead of each reimplementing it.
/// </summary>
internal static class ScenarioParameterBinder
{
    private static readonly HashSet<Type> NumericTypes =
    [
        typeof(sbyte), typeof(byte), typeof(short), typeof(ushort),
        typeof(int), typeof(uint), typeof(long), typeof(ulong),
        typeof(float), typeof(double), typeof(decimal),
    ];

    /// <summary>
    /// Builds <see cref="DiscoveredScenarioParameter"/> metadata for one factory method parameter,
    /// reading <see cref="ScenarioParameterAttribute"/> if present.
    /// </summary>
    public static DiscoveredScenarioParameter BuildParameter(ParameterInfo parameter)
    {
        var attribute = parameter.GetCustomAttribute<ScenarioParameterAttribute>();

        return new DiscoveredScenarioParameter
        {
            Name = parameter.Name!,
            Type = parameter.ParameterType,
            DefaultValue = parameter.HasDefaultValue ? parameter.DefaultValue : null,
            HasDefaultValue = parameter.HasDefaultValue,
            Min = attribute is null || double.IsNaN(attribute.Min) ? null : attribute.Min,
            Max = attribute is null || double.IsNaN(attribute.Max) ? null : attribute.Max,
            Description = attribute?.Description,
        };
    }

    /// <summary>
    /// Builds the argument array for invoking <paramref name="method"/>: <paramref name="leadingArgs"/>
    /// fill the first parameters positionally, then remaining parameters resolve from
    /// <paramref name="overrides"/> (matched by name, case-insensitive), falling back to the
    /// parameter's own default, then a type-appropriate zero value.
    /// </summary>
    public static object?[] BuildArguments(
        string scenarioId,
        MethodInfo method,
        IReadOnlyDictionary<string, object?>? overrides,
        params object[] leadingArgs)
    {
        var parameters = method.GetParameters();
        var args = new object?[parameters.Length];
        for (var i = 0; i < leadingArgs.Length; i++)
        {
            args[i] = leadingArgs[i];
        }

        var overrideMap = overrides is null
            ? null
            : new Dictionary<string, object?>(overrides, StringComparer.OrdinalIgnoreCase);

        for (var i = leadingArgs.Length; i < parameters.Length; i++)
        {
            var parameter = parameters[i];
            if (overrideMap != null && overrideMap.TryGetValue(parameter.Name!, out var overrideValue))
            {
                args[i] = CoerceAndValidateOverride(scenarioId, parameter, overrideValue);
            }
            else if (parameter.HasDefaultValue)
            {
                args[i] = parameter.DefaultValue;
            }
            else
            {
                args[i] = DefaultForType(parameter.ParameterType);
            }
        }

        return args;
    }

    /// <summary>
    /// Humanizes a PascalCase id into space-separated words (e.g. "DiabeticPatient" -> "Diabetic Patient").
    /// </summary>
    public static string Humanize(string id)
    {
        var builder = new StringBuilder();
        foreach (var c in id)
        {
            if (builder.Length > 0 && char.IsUpper(c) && !char.IsUpper(builder[^1]))
                builder.Append(' ');
            builder.Append(c);
        }

        return builder.ToString();
    }

    private static object? DefaultForType(Type type)
    {
        if (type.IsValueType && Nullable.GetUnderlyingType(type) == null)
        {
            return Activator.CreateInstance(type);
        }

        return null;
    }

    [SuppressMessage("Usage", "CA2208:Instantiate argument exceptions correctly", Justification = "paramName intentionally names the public Invoke argument 'parameterOverrides', the surface a caller can fix.")]
    private static object? CoerceAndValidateOverride(string scenarioId, ParameterInfo parameter, object? value)
    {
        var effectiveType = Nullable.GetUnderlyingType(parameter.ParameterType) ?? parameter.ParameterType;

        if (value is null)
        {
            if (parameter.ParameterType.IsValueType && Nullable.GetUnderlyingType(parameter.ParameterType) is null)
            {
                throw new ArgumentException(
                    $"Scenario '{scenarioId}': override for parameter '{parameter.Name}' is null, but the parameter type '{parameter.ParameterType.Name}' is a non-nullable value type.",
                    "parameterOverrides");
            }

            return null;
        }

        if (effectiveType.IsInstanceOfType(value))
        {
            return value;
        }

        if (NumericTypes.Contains(effectiveType) && NumericTypes.Contains(value.GetType()))
        {
            try
            {
                return Convert.ChangeType(value, effectiveType, CultureInfo.InvariantCulture);
            }
            catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException)
            {
                // Falls through to the throw below (e.g. a long value too large for an int parameter).
            }
        }

        throw new ArgumentException(
            $"Scenario '{scenarioId}': override for parameter '{parameter.Name}' is of type '{value.GetType().Name}', but the parameter expects '{effectiveType.Name}'.",
            "parameterOverrides");
    }
}
