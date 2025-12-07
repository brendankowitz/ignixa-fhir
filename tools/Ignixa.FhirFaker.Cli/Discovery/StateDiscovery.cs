using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Ignixa.FhirFakes.Population;
using Ignixa.FhirFakes.Scenarios.States;

namespace Ignixa.FhirFaker.Cli.Discovery;

/// <summary>
/// Discovers available ObservationState factory methods and cities.
/// </summary>
[SuppressMessage("Design", "CA1515:Consider making public types internal", Justification = "Needed for testing")]
public static class StateDiscovery
{
    private static readonly Lazy<Dictionary<string, MethodInfo>> s_observationStates = new(DiscoverObservationStates);
    private static readonly DemographicsDataProvider s_demographics = DemographicsDataProvider.CreateDefault();

    /// <summary>
    /// Gets all available observation state names.
    /// </summary>
    [SuppressMessage("Design", "CA1024:Use properties where appropriate", Justification = "Discovery method may be expensive")]
    public static IEnumerable<string> GetObservationStateNames()
    {
        return s_observationStates.Value.Keys;
    }

    /// <summary>
    /// Creates an ObservationState by name using reflection.
    /// </summary>
    public static ObservationState? CreateObservationState(string name)
    {
        if (s_observationStates.Value.TryGetValue(name, out var method))
        {
            // Get parameters and prepare default values
            var parameters = method.GetParameters();
            var args = new object?[parameters.Length];
            
            for (int i = 0; i < parameters.Length; i++)
            {
                args[i] = parameters[i].DefaultValue;
            }

            // Invoke the static factory method
            var result = method.Invoke(null, args);
            return result as ObservationState;
        }

        return null;
    }

    /// <summary>
    /// Finds a city by name.
    /// </summary>
    public static CityDemographics? FindCity(string cityName)
    {
        return s_demographics.Cities.FirstOrDefault(c => 
            c.Name.Equals(cityName, StringComparison.OrdinalIgnoreCase));
    }

    private static Dictionary<string, MethodInfo> DiscoverObservationStates()
    {
        var states = new Dictionary<string, MethodInfo>(StringComparer.OrdinalIgnoreCase);

        var observationStateType = typeof(ObservationState);
        var methods = observationStateType.GetMethods(BindingFlags.Public | BindingFlags.Static);

        foreach (var method in methods)
        {
            // Look for public static methods that return ObservationState and have no required parameters
            if (method.ReturnType == observationStateType)
            {
                var parameters = method.GetParameters();
                // Only include methods with no required parameters (all parameters have default values)
                if (parameters.All(p => p.HasDefaultValue))
                {
                    states[method.Name] = method;
                }
            }
        }

        return states;
    }
}
