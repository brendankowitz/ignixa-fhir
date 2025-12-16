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

        var resourceTypeArg = new Argument<string>("resourceType") { Description = "The FHIR resource type (e.g., Patient, Observation)" };
        var stateNameArg = new Argument<string?>("stateName") { Description = "Optional state/builder name (e.g., BloodGlucose for Observation)", Arity = ArgumentArity.ZeroOrOne, DefaultValueFactory = _ => null };

        var outOption = new Option<string>("--out") { Description = "Output folder for generated files", Required = true };
        var firstnameOption = new Option<string?>("--firstname") { Description = "Patient first name" };
        var surnameOption = new Option<string?>("--surname") { Description = "Patient surname" };
        var fromOption = new Option<string?>("--from") { Description = "City to generate from" };
        var validateOption = new Option<bool>("--validate") { Description = "Validate generated resource against schema", DefaultValueFactory = _ => false };

        resourceCommand.Arguments.Add(resourceTypeArg);
        resourceCommand.Arguments.Add(stateNameArg);
        resourceCommand.Options.Add(outOption);
        resourceCommand.Options.Add(firstnameOption);
        resourceCommand.Options.Add(surnameOption);
        resourceCommand.Options.Add(fromOption);
        resourceCommand.Options.Add(validateOption);

        resourceCommand.SetAction(async (parseResult, cancellationToken) =>
        {
            var resourceType = parseResult.GetValue(resourceTypeArg)!;
            var stateName = parseResult.GetValue(stateNameArg);
            var outFolder = parseResult.GetValue(outOption)!;
            var firstname = parseResult.GetValue(firstnameOption);
            var surname = parseResult.GetValue(surnameOption);
            var from = parseResult.GetValue(fromOption);
            var validate = parseResult.GetValue(validateOption);
            await HandleResourceCommand(schemaProvider, fhirVersion, resourceType, stateName, outFolder, firstname, surname, from, validate);
        });

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
