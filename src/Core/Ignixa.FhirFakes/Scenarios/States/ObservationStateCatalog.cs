// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Ignixa.FhirFakes.Scenarios;

namespace Ignixa.FhirFakes.Scenarios.States;

/// <summary>
/// Discovers and creates predefined <see cref="ObservationState"/> instances by convention: public
/// static factory methods on <see cref="ObservationState"/> that return <see cref="ObservationState"/>
/// and whose parameters all have default values.
/// </summary>
public static class ObservationStateCatalog
{
    private static readonly Lazy<IReadOnlyDictionary<string, MethodInfo>> s_states = new(Discover);
    private static readonly Lazy<IReadOnlyList<string>> s_names = new(() => [.. s_states.Value.Keys]);

    /// <summary>Gets all available observation state names.</summary>
    [SuppressMessage("Design", "CA1024:Use properties where appropriate", Justification = "Backs lazy reflection-based discovery; a method conveys the work performed and matches ScenarioCatalog.GetAll.")]
    public static IReadOnlyList<string> GetNames() => s_names.Value;

    /// <summary>
    /// Tries to find an observation state factory by name (case-insensitive) and create it using
    /// each parameter's own default value. Returns false only when no state matches the name.
    /// </summary>
    /// <exception cref="ScenarioInvocationException">The state factory itself threw during creation.</exception>
    public static bool TryCreate(string name, [NotNullWhen(true)] out ObservationState? state)
    {
        ArgumentNullException.ThrowIfNull(name);
        state = null;

        if (!s_states.Value.TryGetValue(name, out var method))
            return false;

        var parameters = method.GetParameters();
        var args = new object?[parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
            args[i] = parameters[i].DefaultValue;

        try
        {
            state = (ObservationState)method.Invoke(null, args)!;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw new ScenarioInvocationException(
                $"Observation state '{name}' threw during creation: {ex.InnerException.Message}", ex.InnerException);
        }

        return true;
    }

    private static IReadOnlyDictionary<string, MethodInfo> Discover()
    {
        var states = new Dictionary<string, MethodInfo>(StringComparer.OrdinalIgnoreCase);
        var observationStateType = typeof(ObservationState);

        var methods = observationStateType.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.ReturnType == observationStateType && m.GetParameters().All(p => p.HasDefaultValue));

        foreach (var method in methods)
            states[method.Name] = method;

        return states;
    }
}
