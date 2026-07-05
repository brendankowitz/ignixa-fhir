// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Reflection;

namespace Ignixa.FhirFakes;

/// <summary>
/// Thread-safe set of assemblies to scan for reflection-based discovery, seeded with an owning
/// library's own assembly. Shared by <see cref="Scenarios.ScenarioCatalog"/>,
/// <see cref="Workflow.WorkflowScenarioCatalog"/>, and <see cref="Scenarios.States.ObservationStateCatalog"/>,
/// each of which independently implemented this same lock-protected register-and-snapshot pattern.
/// </summary>
internal sealed class AssemblyRegistry(Assembly seed)
{
    private readonly Lock _lock = new();
    private readonly HashSet<Assembly> _assemblies = [seed];

    /// <summary>
    /// Registers an additional assembly to scan. Idempotent — registering the same assembly more
    /// than once has no additional effect.
    /// </summary>
    public void Register(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        lock (_lock)
        {
            _assemblies.Add(assembly);
        }
    }

    /// <summary>Takes a lock-protected snapshot of the registered assemblies.</summary>
    public Assembly[] Snapshot()
    {
        lock (_lock)
        {
            return [.. _assemblies];
        }
    }
}
