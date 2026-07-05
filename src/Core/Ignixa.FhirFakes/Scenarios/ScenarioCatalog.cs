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
/// types in a <c>*.Scenarios.Predefined</c> namespace whose first parameter is
/// <see cref="IFhirSchemaProvider"/> and that return <see cref="ScenarioContext"/>. A leading "Get" is
/// stripped from the method name to form the scenario id (e.g. "GetDiabeticPatient" -> "DiabeticPatient").
/// Scans this library's own assembly plus any assembly registered via <see cref="RegisterAssembly"/>, so a
/// downstream consumer can ship private scenarios discoverable through this same catalog.
/// </summary>
public static class ScenarioCatalog
{
    private static readonly Lock RegistrationLock = new();
    private static readonly HashSet<Assembly> RegisteredAssemblies = [typeof(DiabeticPatientScenario).Assembly];

    /// <summary>
    /// Attribute-bag key under which <see cref="Invoke"/> records the scenario's
    /// <see cref="DiscoveredScenario.Domain"/> on the returned <see cref="ScenarioContext"/>.
    /// Read via <c>context.GetAttribute&lt;ClinicalDomain&gt;(ScenarioCatalog.ClinicalDomainAttributeKey)</c>.
    /// </summary>
    public const string ClinicalDomainAttributeKey = "clinicalDomain";

    /// <summary>
    /// Registers an additional assembly to scan for predefined scenarios. Idempotent — registering
    /// the same assembly more than once has no additional effect. Scenarios in the registered assembly
    /// follow the same convention as this library's own scenarios: a public static type in a namespace
    /// ending in <c>.Scenarios.Predefined</c> (the namespace need not be under <c>Ignixa.FhirFakes</c> —
    /// it is matched by suffix, not by owning assembly).
    /// </summary>
    public static void RegisterAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        lock (RegistrationLock)
        {
            RegisteredAssemblies.Add(assembly);
        }
    }

    /// <summary>
    /// Gets all discovered scenarios, across this library's assembly and every registered assembly.
    /// </summary>
    [SuppressMessage("Design", "CA1024:Use properties where appropriate", Justification = "Backs reflection-based discovery; a method conveys the work performed and matches ObservationStateCatalog.GetNames.")]
    public static IReadOnlyList<DiscoveredScenario> GetAll() => Discover();

    /// <summary>
    /// Finds a scenario by id (case-insensitive). Returns <see langword="null"/> if no scenario matches —
    /// this is expected control flow for an unknown id, not an error.
    /// </summary>
    public static DiscoveredScenario? Find(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return Discover().FirstOrDefault(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
    }

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
        Assembly[] assemblies;
        lock (RegistrationLock)
        {
            assemblies = [.. RegisteredAssemblies];
        }

        var scenarios = new List<DiscoveredScenario>();

        foreach (var assembly in assemblies)
        {
            var scenarioTypes = assembly.GetTypes()
                .Where(t => t.Namespace is not null
                    && t.Namespace.EndsWith(".Scenarios.Predefined", StringComparison.Ordinal)
                    && t.IsClass && t.IsPublic);

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
        }

        return scenarios;
    }
}
