using System.CommandLine;
using Ignixa.FhirFaker.Cli.Commands;
using Ignixa.Specification.Generated;

namespace Ignixa.FhirFaker.Cli;

/// <summary>
/// Entry point for the FHIR Faker CLI tool.
/// </summary>
class Program
{
    static async Task<int> Main(string[] args)
    {
        // Initialize the schema provider (using R4 specification)
        var schemaProvider = new R4CoreSchemaProvider();

        // Create root command
        var rootCommand = new RootCommand("FHIR Faker - Generate realistic FHIR test data")
        {
            ResourceCommand.Create(schemaProvider),
            ScenarioCommand.Create(schemaProvider),
            PopulationCommand.Create(schemaProvider)
        };

        return await rootCommand.InvokeAsync(args);
    }
}
