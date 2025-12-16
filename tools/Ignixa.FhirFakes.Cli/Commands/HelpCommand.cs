using System.CommandLine;
using Ignixa.FhirFakes.Cli.Discovery;

namespace Ignixa.FhirFakes.Cli.Commands;

/// <summary>
/// Command for displaying context-specific help.
/// </summary>
internal static class HelpCommand
{
    public static Command Create()
    {
        var helpCommand = new Command("help", "Display detailed help for commands and available options");

        var topicArg = new Argument<string?>("topic")
        {
            Description = "Topic to get help on (e.g., 'scenarios', 'states', 'cities')",
            Arity = ArgumentArity.ZeroOrOne, DefaultValueFactory = _ => null
        };
        

        helpCommand.Arguments.Add(topicArg);

        helpCommand.SetAction(parseResult =>
        {
            var topic = parseResult.GetValue(topicArg);
            HandleHelpCommand(topic);
        });

        return helpCommand;
    }

    private static void HandleHelpCommand(string? topic)
    {
        if (string.IsNullOrEmpty(topic))
        {
            ShowGeneralHelp();
            return;
        }

        if (topic.Equals("scenarios", StringComparison.OrdinalIgnoreCase) || 
            topic.Equals("scenario", StringComparison.OrdinalIgnoreCase))
        {
            ShowScenarios();
        }
        else if (topic.Equals("states", StringComparison.OrdinalIgnoreCase) || 
                 topic.Equals("state", StringComparison.OrdinalIgnoreCase) ||
                 topic.Equals("observations", StringComparison.OrdinalIgnoreCase) ||
                 topic.Equals("observation", StringComparison.OrdinalIgnoreCase))
        {
            ShowObservationStates();
        }
        else if (topic.Equals("cities", StringComparison.OrdinalIgnoreCase) || 
                 topic.Equals("city", StringComparison.OrdinalIgnoreCase) ||
                 topic.Equals("locations", StringComparison.OrdinalIgnoreCase) ||
                 topic.Equals("location", StringComparison.OrdinalIgnoreCase))
        {
            ShowCities();
        }
        else if (topic.Equals("versions", StringComparison.OrdinalIgnoreCase) || 
                 topic.Equals("version", StringComparison.OrdinalIgnoreCase))
        {
            ShowVersions();
        }
        else
        {
            Console.WriteLine($"Unknown help topic: {topic}");
            Console.WriteLine();
            ShowGeneralHelp();
        }
    }

    private static void ShowGeneralHelp()
    {
        Console.WriteLine("FHIR Fakes - Generate realistic FHIR test data");
        Console.WriteLine();
        Console.WriteLine("Available help topics:");
        Console.WriteLine("  ignixa-fakes help scenarios    - List all available predefined scenarios");
        Console.WriteLine("  ignixa-fakes help states        - List all available observation states");
        Console.WriteLine("  ignixa-fakes help cities        - List all available cities for population generation");
        Console.WriteLine("  ignixa-fakes help versions      - Show supported FHIR versions");
        Console.WriteLine();
        Console.WriteLine("For command-specific help, use:");
        Console.WriteLine("  ignixa-fakes r4 resource --help");
        Console.WriteLine("  ignixa-fakes r4 scenario --help");
        Console.WriteLine("  ignixa-fakes r4 population --help");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  ignixa-fakes r4 resource Patient --out ./output --firstname Bob");
        Console.WriteLine("  ignixa-fakes r5 scenario DiabeticPatient --out ./output --resolved-references");
        Console.WriteLine("  ignixa-fakes r4 population --out ./output --from Seattle --count 100");
    }

    private static void ShowScenarios()
    {
        Console.WriteLine("Available Predefined Scenarios:");
        Console.WriteLine();
        
        var scenarios = ScenarioDiscovery.GetScenarioNames().OrderBy(s => s).ToList();
        
        Console.WriteLine($"Found {scenarios.Count} scenarios:");
        Console.WriteLine();
        
        foreach (var scenario in scenarios)
        {
            Console.WriteLine($"  - {scenario}");
        }
        
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine($"  ignixa-fakes r4 scenario <ScenarioName> --out <folder> [--resolved-references]");
        Console.WriteLine();
        Console.WriteLine("Example:");
        Console.WriteLine("  ignixa-fakes r4 scenario DiabeticPatient --out ./output --resolved-references");
    }

    private static void ShowObservationStates()
    {
        Console.WriteLine("Available Observation States:");
        Console.WriteLine();
        
        var states = StateDiscovery.GetObservationStateNames().OrderBy(s => s).ToList();
        
        Console.WriteLine($"Found {states.Count} observation states:");
        Console.WriteLine();
        
        foreach (var state in states)
        {
            Console.WriteLine($"  - {state}");
        }
        
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine($"  ignixa-fakes r4 resource Observation <StateName> --out <folder>");
        Console.WriteLine();
        Console.WriteLine("Example:");
        Console.WriteLine("  ignixa-fakes r4 resource Observation BloodGlucose --out ./output");
    }

    private static void ShowCities()
    {
        Console.WriteLine("Available Cities for Population Generation:");
        Console.WriteLine();
        
        var demographics = Ignixa.FhirFakes.Population.DemographicsDataProvider.CreateDefault();
        var cities = demographics.Cities.OrderBy(c => c.State).ThenBy(c => c.Name).ToList();
        
        Console.WriteLine($"Found {cities.Count} cities across {demographics.States.Count} states:");
        Console.WriteLine();
        
        var citiesByState = cities.GroupBy(c => c.State).OrderBy(g => g.Key);
        foreach (var stateGroup in citiesByState)
        {
            Console.WriteLine($"{stateGroup.Key}:");
            foreach (var city in stateGroup)
            {
                Console.WriteLine($"  - {city.Name} (population: {city.Population:N0})");
            }
            Console.WriteLine();
        }
        
        Console.WriteLine("Usage:");
        Console.WriteLine($"  ignixa-fakes r4 population --out <folder> --from <city|state> --count <number>");
        Console.WriteLine();
        Console.WriteLine("Example:");
        Console.WriteLine("  ignixa-fakes r4 population --out ./output --from Seattle --count 100");
    }

    private static void ShowVersions()
    {
        Console.WriteLine("Supported FHIR Versions:");
        Console.WriteLine();
        Console.WriteLine("  - stu3  - FHIR STU3 (v3.0.2)");
        Console.WriteLine("  - r4    - FHIR R4 (v4.0.1)");
        Console.WriteLine("  - r4b   - FHIR R4B (v4.3.0)");
        Console.WriteLine("  - r5    - FHIR R5 (v5.0.0)");
        Console.WriteLine("  - r6    - FHIR R6 (v6.0.0)");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  ignixa-fakes <version> <command> --out <folder> [options]");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  ignixa-fakes r4 resource Patient --out ./output");
        Console.WriteLine("  ignixa-fakes r5 scenario WellnessVisit --out ./output");
        Console.WriteLine("  ignixa-fakes r6 population --out ./output --from Boston --count 50");
    }
}
