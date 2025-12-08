using System.CommandLine;
using System.Text.Json;
using Ignixa.FhirFakes.Cli.Discovery;
using Ignixa.FhirFakes;
using Ignixa.FhirFakes.Builders;
using Ignixa.Serialization;
using Ignixa.Specification;

namespace Ignixa.FhirFakes.Cli.Commands;

/// <summary>
/// Command for generating single FHIR resources.
/// </summary>
internal static class ResourceCommand
{
    public static Command Create(IFhirSchemaProvider schemaProvider, string fhirVersion)
    {
        var resourceCommand = new Command("resource", "Generate a single FHIR resource");

        var resourceTypeArg = new Argument<string>("resourceType", "The FHIR resource type (e.g., Patient, Observation)");
        var stateNameArg = new Argument<string?>("stateName", () => null, "Optional state/builder name (e.g., BloodGlucose for Observation)");

        var outOption = new Option<string>("--out", "Output folder for generated files") { IsRequired = true };
        var firstnameOption = new Option<string?>("--firstname", "Patient first name");
        var surnameOption = new Option<string?>("--surname", "Patient surname");
        var fromOption = new Option<string?>("--from", "City to generate from");
        var validateOption = new Option<bool>("--validate", () => false, "Validate generated resource against schema");

        resourceCommand.AddArgument(resourceTypeArg);
        resourceCommand.AddArgument(stateNameArg);
        resourceCommand.AddOption(outOption);
        resourceCommand.AddOption(firstnameOption);
        resourceCommand.AddOption(surnameOption);
        resourceCommand.AddOption(fromOption);
        resourceCommand.AddOption(validateOption);

        resourceCommand.SetHandler(async (resourceType, stateName, outFolder, firstname, surname, from, validate) =>
        {
            await HandleResourceCommand(schemaProvider, fhirVersion, resourceType, stateName, outFolder, firstname, surname, from, validate);
        }, resourceTypeArg, stateNameArg, outOption, firstnameOption, surnameOption, fromOption, validateOption);

        return resourceCommand;
    }

    private static async Task HandleResourceCommand(
        IFhirSchemaProvider schemaProvider,
        string fhirVersion,
        string resourceType,
        string? stateName,
        string outFolder,
        string? firstname,
        string? surname,
        string? from,
        bool validate)
    {
        try
        {
            // Ensure output directory exists
            Directory.CreateDirectory(outFolder);

            JsonSerializerOptions options = new()
            {
                WriteIndented = true
            };

            if (resourceType.Equals("Patient", StringComparison.OrdinalIgnoreCase))
            {
                // Use PatientBuilder
                var builder = PatientBuilderFactory.Create(schemaProvider);

                if (!string.IsNullOrEmpty(firstname))
                    builder.WithGivenName(firstname);
                
                if (!string.IsNullOrEmpty(surname))
                    builder.WithFamilyName(surname);

                if (!string.IsNullOrEmpty(from))
                {
                    // Try to find city in known cities
                    var city = StateDiscovery.FindCity(from);
                    if (city != null)
                    {
                        builder.FromCity(city);
                    }
                    else
                    {
                        builder.WithCity(from);
                    }
                }

                var patient = builder.Build();
                var id = patient.MutableNode["id"]?.ToString() ?? Guid.NewGuid().ToString();
                var filename = $"{fhirVersion}-patient-{id}.json";
                var outputPath = Path.Combine(outFolder, filename);

                var json = JsonSerializer.Serialize(patient.MutableNode, options);
                await File.WriteAllTextAsync(outputPath, json);

                Console.WriteLine($"✓ Generated Patient: {outputPath}");

                // Validate the generated resource
                if (validate)
                {
                    var validationResult = ValidationHelper.ValidateResource(patient.MutableNode, schemaProvider);
                    if (!validationResult.IsValid)
                    {
                        Console.WriteLine($"\n⚠️  Validation Issues Detected:");
                        ValidationHelper.DisplayResults(validationResult, "Patient", fhirVersion, verbose: false);
                    }
                    else
                    {
                        Console.WriteLine($"✓ Validation passed");
                    }
                }
            }
            else if (resourceType.Equals("Observation", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(stateName))
            {
                // Use ObservationState factory method
                var observationState = StateDiscovery.CreateObservationState(stateName);
                if (observationState == null)
                {
                    Console.WriteLine($"✗ Unknown observation state: {stateName}");
                    Console.WriteLine("Available states:");
                    foreach (var name in StateDiscovery.GetObservationStateNames())
                    {
                        Console.WriteLine($"  - {name}");
                    }
                    return;
                }

                // Create a minimal scenario to execute the state
                var faker = new SchemaBasedFhirResourceFaker(schemaProvider);
                var context = new Ignixa.FhirFakes.Scenarios.ScenarioContext();
                
                // Create a minimal patient
                var patient = PatientBuilderFactory.Create(schemaProvider).Build();
                context.Patient = patient;

                // Execute the observation state
                observationState.Execute(context, faker);

                // Get the last resource (the observation) - use count instead of LastOrDefault
                var allResources = context.AllResources;
                if (allResources.Count > 0)
                {
                    var observation = allResources[allResources.Count - 1];
                    var id = observation.MutableNode["id"]?.ToString() ?? Guid.NewGuid().ToString();
                    var filename = $"{fhirVersion}-observation-{stateName}-{id}.json";
                    var outputPath = Path.Combine(outFolder, filename);

                    var json = JsonSerializer.Serialize(observation.MutableNode, options);
                    await File.WriteAllTextAsync(outputPath, json);

                    Console.WriteLine($"✓ Generated Observation ({stateName}): {outputPath}");

                    // Validate the generated resource
                    if (validate)
                    {
                        var validationResult = ValidationHelper.ValidateResource(observation.MutableNode, schemaProvider);
                        if (!validationResult.IsValid)
                        {
                            Console.WriteLine($"\n⚠️  Validation Issues Detected:");
                            ValidationHelper.DisplayResults(validationResult, "Observation", fhirVersion, verbose: false);
                        }
                        else
                        {
                            Console.WriteLine($"✓ Validation passed");
                        }
                    }
                }
            }
            else
            {
                Console.WriteLine($"✗ Resource type '{resourceType}' is not supported or requires a state name.");
                Console.WriteLine("Supported: Patient, Observation <stateName>");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Error: {ex.Message}");
        }
    }
}
