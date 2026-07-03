// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Reflection;

namespace Ignixa.FhirFakes.Scenarios.States;

/// <summary>
/// Discovers and creates predefined <see cref="ObservationState"/> instances by convention: public
/// static factory methods on <see cref="ObservationState"/> that return <see cref="ObservationState"/>
/// and whose parameters all have default values.
/// </summary>
public static class ObservationStateCatalog
{
    private static readonly Lazy<IReadOnlyDictionary<string, MethodInfo>> s_states = new(Discover);

    /// <summary>
    /// Gets all available observation state names.
    /// </summary>
    public static IReadOnlyList<string> Names() => s_states.Value.Keys.ToList();

    /// <summary>
    /// Creates an <see cref="ObservationState"/> by name (case-insensitive), using each factory
    /// parameter's own default value. Returns <see langword="null"/> if no state matches.
    /// </summary>
    public static ObservationState? Create(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (!s_states.Value.TryGetValue(name, out var method))
            return null;

        var parameters = method.GetParameters();
        var args = new object?[parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
            args[i] = parameters[i].DefaultValue;

        return method.Invoke(null, args) as ObservationState;
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
