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
/// types in the <c>Ignixa.FhirFakes.Workflow.Predefined</c> namespace whose first two parameters are
/// <see cref="IFhirSchemaProvider"/> and <see cref="WorkflowScenarioOptions"/> and that return
/// <see cref="WorkflowScenarioResult"/>. Sibling of <see cref="ScenarioCatalog"/> rather than a
/// generalization of it — the two return different result types. Scans only its own assembly for
/// now; external-assembly registration is not implemented in this catalog yet (no second consumer
/// exists to justify the extra surface — see the investigation doc's Phase 5).
/// </summary>
public static class WorkflowScenarioCatalog
{
    private static readonly Lazy<IReadOnlyList<DiscoveredScenario>> Scenarios = new(Discover);

    /// <summary>Gets all discovered workflow scenario packs.</summary>
    [SuppressMessage("Design", "CA1024:Use properties where appropriate", Justification = "Backs lazy reflection-based discovery; a method conveys the work performed and matches ScenarioCatalog.GetAll.")]
    public static IReadOnlyList<DiscoveredScenario> GetAll() => Scenarios.Value;

    /// <summary>Finds a workflow scenario pack by id (case-insensitive), or null if none matches.</summary>
    public static DiscoveredScenario? Find(string id) =>
        Scenarios.Value.FirstOrDefault(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

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
        var assembly = typeof(DailyAppointmentScheduleScenario).Assembly;

        var packTypes = assembly.GetTypes()
            .Where(t => t.Namespace == "Ignixa.FhirFakes.Workflow.Predefined" && t.IsClass && t.IsPublic);

        var scenarios = new List<DiscoveredScenario>();

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

        return scenarios;
    }
}
