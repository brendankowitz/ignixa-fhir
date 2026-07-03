// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Reflection;
using System.Text;
using Ignixa.Abstractions;
using Ignixa.FhirFakes.Scenarios.Predefined;

namespace Ignixa.FhirFakes.Scenarios;

/// <summary>
/// Discovers and invokes predefined FHIR scenarios by convention: public static extension methods on
/// types in the <c>Ignixa.FhirFakes.Scenarios.Predefined</c> namespace whose first parameter is
/// <see cref="IFhirSchemaProvider"/> and that return <see cref="ScenarioContext"/>. A leading "Get" is
/// stripped from the method name to form the scenario id (e.g. "GetDiabeticPatient" -> "DiabeticPatient").
/// </summary>
public static class ScenarioCatalog
{
    private static readonly Lazy<IReadOnlyList<DiscoveredScenario>> s_scenarios = new(Discover);

    /// <summary>
    /// Gets all discovered scenarios.
    /// </summary>
    public static IReadOnlyList<DiscoveredScenario> All() => s_scenarios.Value;

    /// <summary>
    /// Finds a scenario by id (case-insensitive). Returns <see langword="null"/> if no scenario matches —
    /// this is expected control flow for an unknown id, not an error.
    /// </summary>
    public static DiscoveredScenario? Find(string id) =>
        s_scenarios.Value.FirstOrDefault(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Invokes a discovered scenario's factory method, applying <paramref name="parameterOverrides"/>
    /// (matched by parameter name) over the method's own default values. A parameter with neither an
    /// override nor a default falls back to a type-appropriate value (0 for <see langword="int"/>, false
    /// for <see langword="bool"/>, null otherwise) instead of passing reflection's uninitialized
    /// sentinel through.
    /// </summary>
    /// <exception cref="ScenarioInvocationException">
    /// The scenario's factory method itself threw during invocation. The original exception is available
    /// via <see cref="Exception.InnerException"/>.
    /// </exception>
    public static ScenarioContext Invoke(
        DiscoveredScenario scenario,
        IFhirSchemaProvider schemaProvider,
        IReadOnlyDictionary<string, object?>? parameterOverrides = null)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(schemaProvider);

        var parameters = scenario.Method.GetParameters();
        var args = new object?[parameters.Length];
        args[0] = schemaProvider;

        for (var i = 1; i < parameters.Length; i++)
        {
            var parameter = parameters[i];
            if (parameterOverrides != null && parameterOverrides.TryGetValue(parameter.Name!, out var overrideValue))
            {
                args[i] = overrideValue;
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

        try
        {
            return (ScenarioContext)scenario.Method.Invoke(null, args)!;
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            throw new ScenarioInvocationException(
                $"Scenario '{scenario.Id}' threw during invocation: {ex.InnerException.Message}", ex.InnerException);
        }
    }

    private static object? DefaultForType(Type type)
    {
        if (type == typeof(int))
            return 0;
        if (type == typeof(bool))
            return false;
        return null;
    }

    private static IReadOnlyList<DiscoveredScenario> Discover()
    {
        var assembly = typeof(DiabeticPatientScenario).Assembly;

        var scenarioTypes = assembly.GetTypes()
            .Where(t => t.Namespace == "Ignixa.FhirFakes.Scenarios.Predefined" && t.IsClass && t.IsPublic);

        var scenarios = new List<DiscoveredScenario>();

        foreach (var type in scenarioTypes)
        {
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.ReturnType == typeof(ScenarioContext));

            foreach (var method in methods)
            {
                var parameters = method.GetParameters();
                if (parameters.Length == 0 || parameters[0].ParameterType != typeof(IFhirSchemaProvider))
                    continue;

                var id = method.Name.StartsWith("Get", StringComparison.Ordinal)
                    ? method.Name["Get".Length..]
                    : method.Name;

                var attribute = method.GetCustomAttribute<ScenarioAttribute>();

                scenarios.Add(new DiscoveredScenario
                {
                    Id = id,
                    Category = attribute?.Category,
                    Title = attribute?.Title ?? Humanize(id),
                    Description = attribute?.Description,
                    Parameters = parameters.Skip(1).Select(BuildParameter).ToList(),
                    Method = method,
                });
            }
        }

        return scenarios;
    }

    private static DiscoveredScenarioParameter BuildParameter(ParameterInfo parameter)
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

    private static string Humanize(string id)
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
}
