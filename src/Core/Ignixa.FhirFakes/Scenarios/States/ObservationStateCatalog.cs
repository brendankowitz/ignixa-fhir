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
/// static factory methods that return <see cref="ObservationState"/> and whose parameters all have
/// default values. Scans this library's own assembly plus any assembly registered via
/// <see cref="RegisterAssembly"/>, so a downstream consumer can ship private observation states
/// discoverable through this same catalog.
/// </summary>
/// <remarks>
/// Unlike <see cref="Scenarios.ScenarioCatalog"/> and <see cref="Workflow.WorkflowScenarioCatalog"/>,
/// this catalog scans every public type in each registered assembly with no namespace filter. That's
/// intentional, not an oversight of the shared-registry convergence work: the all-default-parameters
/// shape (a zero-argument-invokable factory) is already distinctive enough to identify observation
/// state factories, whereas scenario/workflow packs require a namespace convention because their
/// required-first-parameter shape alone would match too many unrelated methods.
/// </remarks>
public static class ObservationStateCatalog
{
    private static readonly AssemblyRegistry Registry = new(typeof(ObservationState).Assembly);

    /// <summary>
    /// Registers an additional assembly to scan for observation state factories. Idempotent —
    /// registering the same assembly more than once has no additional effect. Factories in the
    /// registered assembly follow the same convention as this library's own factories: a public static
    /// method, on any public type, returning <see cref="ObservationState"/> with all-default parameters.
    /// </summary>
    public static void RegisterAssembly(Assembly assembly) => Registry.Register(assembly);

    /// <summary>Gets all available observation state names, across this library's assembly and every registered assembly.</summary>
    [SuppressMessage("Design", "CA1024:Use properties where appropriate", Justification = "Backs reflection-based discovery; a method conveys the work performed and matches ScenarioCatalog.GetAll.")]
    public static IReadOnlyList<string> GetNames() => [.. Discover().Keys];

    /// <summary>
    /// Tries to find an observation state factory by name (case-insensitive) and create it using
    /// each parameter's own default value. Returns false only when no state matches the name.
    /// </summary>
    /// <exception cref="ScenarioInvocationException">The state factory itself threw during creation.</exception>
    public static bool TryCreate(string name, [NotNullWhen(true)] out ObservationState? state)
    {
        ArgumentNullException.ThrowIfNull(name);
        state = null;

        if (!Discover().TryGetValue(name, out var method))
            return false;

        var parameters = method.GetParameters();
        var args = new object?[parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
            args[i] = parameters[i].DefaultValue;

        object? result;
        try
        {
            result = method.Invoke(null, args);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw new ScenarioInvocationException(
                $"Observation state '{name}' threw during creation: {ex.InnerException.Message}", ex.InnerException);
        }

        // Honor the [NotNullWhen(true)] contract: if the factory ever returned null, `state` must
        // not be non-null-but-actually-null on a true return -- report "no state" instead.
        if (result is null)
        {
            return false;
        }

        state = (ObservationState)result;
        return true;
    }

    private static IReadOnlyDictionary<string, MethodInfo> Discover()
    {
        var assemblies = Registry.Snapshot();

        var states = new Dictionary<string, MethodInfo>(StringComparer.OrdinalIgnoreCase);
        var observationStateType = typeof(ObservationState);

        foreach (var assembly in assemblies)
        {
            var methods = assembly.GetTypes()
                .Where(t => t.IsClass && t.IsPublic)
                .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
                .Where(m => m.ReturnType == observationStateType
                    && !m.IsGenericMethodDefinition
                    && !m.IsSpecialName
                    && m.GetParameters().All(p => p.HasDefaultValue));

            foreach (var method in methods)
                states[method.Name] = method;
        }

        return states;
    }
}
