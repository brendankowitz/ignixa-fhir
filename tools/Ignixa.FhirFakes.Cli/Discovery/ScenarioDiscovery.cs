using Ignixa.Abstractions;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Ignixa.FhirFakes.Scenarios;
using Ignixa.FhirFakes.Scenarios.Predefined;
using Ignixa.Specification;

namespace Ignixa.FhirFakes.Cli.Discovery;

/// <summary>
/// Discovers available predefined scenario methods by convention.
/// </summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal", Justification = "Needed for testing")]
public static class ScenarioDiscovery
{
    private static readonly Lazy<Dictionary<string, ScenarioMethodInfo>> s_scenarios = new(DiscoverScenarios);

    private class ScenarioMethodInfo
    {
        public required MethodInfo Method { get; init; }
        public required Type DeclaringType { get; init; }
    }

    /// <summary>
    /// Gets all available scenario names.
    /// </summary>
    [SuppressMessage("Design", "CA1024:Use properties where appropriate", Justification = "Discovery method may be expensive")]
    public static IEnumerable<string> GetScenarioNames()
    {
        return s_scenarios.Value.Keys;
    }

    /// <summary>
    /// Creates a scenario by name using reflection.
    /// </summary>
    public static ScenarioContext? CreateScenario(IFhirSchemaProvider schemaProvider, string name)
    {
        if (s_scenarios.Value.TryGetValue(name, out var scenarioInfo))
        {
            try
            {
                // Prepare parameters with defaults
                var parameters = scenarioInfo.Method.GetParameters();
                var args = new object?[parameters.Length];

                // First parameter is always IFhirSchemaProvider
                args[0] = schemaProvider;

                // Fill in default values for remaining parameters
                for (int i = 1; i < parameters.Length; i++)
                {
                    if (parameters[i].HasDefaultValue)
                    {
                        args[i] = parameters[i].DefaultValue;
                    }
                    else
                    {
                        // Use default for common types
                        var paramType = parameters[i].ParameterType;
                        if (paramType == typeof(int))
                            args[i] = 0;
                        else if (paramType == typeof(string))
                            args[i] = null;
                        else if (paramType == typeof(bool))
                            args[i] = false;
                        else
                            args[i] = null;
                    }
                }

                // Invoke the static method
                var result = scenarioInfo.Method.Invoke(null, args);
                return result as ScenarioContext;
            }
            catch (Exception)
            {
                // Return null if invocation fails - the caller will handle the null
                return null;
            }
        }

        return null;
    }

    private static Dictionary<string, ScenarioMethodInfo> DiscoverScenarios()
    {
        var scenarios = new Dictionary<string, ScenarioMethodInfo>(StringComparer.OrdinalIgnoreCase);

        // Get the assembly containing the predefined scenarios
        var assembly = typeof(DiabeticPatientScenario).Assembly;

        // Find all types in the Predefined namespace
        var scenarioTypes = assembly.GetTypes()
            .Where(t => t.Namespace == "Ignixa.FhirFakes.Scenarios.Predefined" && t.IsClass && t.IsPublic);

        foreach (var type in scenarioTypes)
        {
            // Find all public static methods that return ScenarioContext
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.ReturnType == typeof(ScenarioContext));

            foreach (var method in methods)
            {
                var parameters = method.GetParameters();
                
                // First parameter must be IFhirSchemaProvider
                if (parameters.Length > 0 && parameters[0].ParameterType == typeof(IFhirSchemaProvider))
                {
                    // Extract friendly name from method name
                    // e.g., "GetDiabeticPatient" -> "DiabeticPatient"
                    var scenarioName = method.Name;
                    if (scenarioName.StartsWith("Get"))
                    {
                        scenarioName = scenarioName.Substring(3);
                    }

                    scenarios[scenarioName] = new ScenarioMethodInfo
                    {
                        Method = method,
                        DeclaringType = type
                    };
                }
            }
        }

        return scenarios;
    }
}
