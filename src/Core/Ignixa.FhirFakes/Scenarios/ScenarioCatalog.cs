// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using System.Reflection;
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
    private static readonly Lazy<IReadOnlyList<DiscoveredScenario>> Scenarios = new(Discover);

    /// <summary>
    /// Attribute-bag key under which <see cref="Invoke"/> records the scenario's
    /// <see cref="DiscoveredScenario.Domain"/> on the returned <see cref="ScenarioContext"/>.
    /// Read via <c>context.GetAttribute&lt;ClinicalDomain&gt;(ScenarioCatalog.ClinicalDomainAttributeKey)</c>.
    /// </summary>
    public const string ClinicalDomainAttributeKey = "clinicalDomain";

    /// <summary>
    /// Gets all discovered scenarios.
    /// </summary>
    [SuppressMessage("Design", "CA1024:Use properties where appropriate", Justification = "Backs lazy reflection-based discovery; a method conveys the work performed and matches ObservationStateCatalog.GetNames.")]
    public static IReadOnlyList<DiscoveredScenario> GetAll() => Scenarios.Value;

    /// <summary>
    /// Finds a scenario by id (case-insensitive). Returns <see langword="null"/> if no scenario matches —
    /// this is expected control flow for an unknown id, not an error.
    /// </summary>
    public static DiscoveredScenario? Find(string id) =>
        Scenarios.Value.FirstOrDefault(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

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

        var args = ScenarioParameterBinder.BuildArguments(scenario.Id, scenario.Method, parameterOverrides, "parameterOverrides", schemaProvider);

        ScenarioContext context;
        try
        {
            context = (ScenarioContext)scenario.Method.Invoke(null, args)!;
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            throw new ScenarioInvocationException(
                $"Scenario '{scenario.Id}' threw during invocation: {ex.InnerException.Message}", ex.InnerException);
        }

        if (scenario.Domain is { } domain)
        {
            context.SetAttribute(ClinicalDomainAttributeKey, domain);
        }

        return context;
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

                var attribute = method.GetCustomAttribute<ScenarioAttribute>();
                var id = attribute?.Id
                    ?? (method.Name.StartsWith("Get", StringComparison.Ordinal) ? method.Name["Get".Length..] : method.Name);

                scenarios.Add(new DiscoveredScenario
                {
                    Id = id,
                    Category = attribute?.Category,
                    Title = attribute?.Title ?? ScenarioParameterBinder.Humanize(id),
                    Description = attribute?.Description,
                    Parameters = parameters.Skip(1).Select(ScenarioParameterBinder.BuildParameter).ToList(),
                    Domain = attribute is null || attribute.Domain == ClinicalDomain.Unspecified ? null : attribute.Domain,
                    Method = method,
                });
            }
        }

        return scenarios;
    }
}
