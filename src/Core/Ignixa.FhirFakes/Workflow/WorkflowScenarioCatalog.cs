// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Ignixa.Abstractions;
using Ignixa.FhirFakes.Scenarios;
using Ignixa.FhirFakes.Workflow.Predefined;

namespace Ignixa.FhirFakes.Workflow;

/// <summary>
/// Discovers and invokes predefined workflow scenario packs by convention: public static methods on
/// public types in a <c>*.Workflow.Predefined</c> namespace whose first two parameters are
/// <see cref="IFhirSchemaProvider"/> and <see cref="WorkflowScenarioOptions"/> and that return
/// <see cref="WorkflowScenarioResult"/>. Sibling of <see cref="ScenarioCatalog"/> rather than a
/// generalization of it — the two return different result types. Scans this library's own assembly
/// plus any assembly registered via <see cref="RegisterAssembly"/>, so a downstream consumer can ship
/// private workflow packs (e.g. its own enrichers/composers layered on <see cref="WorkflowGraphBuilder"/>)
/// discoverable through this same catalog instead of forking scenario-discovery logic.
/// </summary>
public static class WorkflowScenarioCatalog
{
    private static readonly AssemblyRegistry Registry = new(typeof(DailyAppointmentScheduleScenario).Assembly);

    /// <summary>
    /// Registers an additional assembly to scan for workflow scenario packs. Idempotent — registering
    /// the same assembly more than once has no additional effect. Scenario packs in the registered
    /// assembly follow the same convention as this library's own packs: a public static type in a
    /// namespace ending in <c>.Workflow.Predefined</c> (the namespace need not be under
    /// <c>Ignixa.FhirFakes</c> — it is matched by suffix, not by owning assembly).
    /// </summary>
    public static void RegisterAssembly(Assembly assembly) => Registry.Register(assembly);

    /// <summary>Gets all discovered workflow scenario packs, across this library's assembly and every registered assembly.</summary>
    [SuppressMessage("Design", "CA1024:Use properties where appropriate", Justification = "Backs reflection-based discovery; a method conveys the work performed and matches ScenarioCatalog.GetAll.")]
    public static IReadOnlyList<DiscoveredScenario> GetAll() => Discover();

    /// <summary>Finds a workflow scenario pack by id (case-insensitive), or null if none matches.</summary>
    public static DiscoveredScenario? Find(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return Discover().FirstOrDefault(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Invokes a discovered workflow scenario pack's factory method, applying
    /// <paramref name="parameterOverrides"/> over the method's own defaults.
    /// </summary>
    /// <exception cref="ScenarioInvocationException">The pack's factory method threw during invocation.</exception>
    public static WorkflowScenarioResult Invoke(
        DiscoveredScenario scenario,
        IFhirSchemaProvider schemaProvider,
        WorkflowScenarioOptions options,
        IReadOnlyDictionary<string, object?>? parameterOverrides = null)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(schemaProvider);
        ArgumentNullException.ThrowIfNull(options);

        var args = ScenarioParameterBinder.BuildArguments(
            scenario.Id, scenario.Method, parameterOverrides, "parameterOverrides", schemaProvider, options);

        try
        {
            return (WorkflowScenarioResult)scenario.Method.Invoke(null, args)!;
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            throw new ScenarioInvocationException(
                $"Workflow scenario '{scenario.Id}' threw during invocation: {ex.InnerException.Message}", ex.InnerException);
        }
    }

    private static IReadOnlyList<DiscoveredScenario> Discover()
    {
        var assemblies = Registry.Snapshot();

        var scenarios = new List<DiscoveredScenario>();

        foreach (var assembly in assemblies)
        {
            var packTypes = assembly.GetTypes()
                .Where(t => t.Namespace is not null
                    && t.Namespace.EndsWith(".Workflow.Predefined", StringComparison.Ordinal)
                    && t.IsClass && t.IsPublic);

            foreach (var type in packTypes)
            {
                var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .Where(m => m.ReturnType == typeof(WorkflowScenarioResult));

                foreach (var method in methods)
                {
                    var parameters = method.GetParameters();
                    if (parameters.Length < 2
                        || parameters[0].ParameterType != typeof(IFhirSchemaProvider)
                        || parameters[1].ParameterType != typeof(WorkflowScenarioOptions))
                    {
                        continue;
                    }

                    var attribute = method.GetCustomAttribute<ScenarioAttribute>();
                    var id = attribute?.Id
                        ?? (method.Name.StartsWith("Get", StringComparison.Ordinal) ? method.Name["Get".Length..] : method.Name);

                    scenarios.Add(new DiscoveredScenario
                    {
                        Id = id,
                        Category = attribute?.Category,
                        Title = attribute?.Title ?? ScenarioParameterBinder.Humanize(id),
                        Description = attribute?.Description,
                        Parameters = parameters.Skip(2).Select(ScenarioParameterBinder.BuildParameter).ToList(),
                        Domain = attribute is null || attribute.Domain == ClinicalDomain.Unspecified ? null : attribute.Domain,
                        Method = method,
                    });
                }
            }
        }

        return scenarios;
    }
}
