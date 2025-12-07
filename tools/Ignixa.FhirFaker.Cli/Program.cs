using System.CommandLine;
using Ignixa.FhirFaker.Cli.Commands;
using Ignixa.Specification;
using Ignixa.Specification.Generated;

namespace Ignixa.FhirFaker.Cli;

/// <summary>
/// Entry point for the FHIR Faker CLI tool.
/// </summary>
class Program
{
    static async Task<int> Main(string[] args)
    {
        // Create root command
        var rootCommand = new RootCommand("FHIR Faker - Generate realistic FHIR test data");

        // Create R4 subcommand with its own subcommands
        var r4Command = new Command("r4", "Use FHIR R4 specification");
        var r4SchemaProvider = new R4CoreSchemaProvider();
        r4Command.AddCommand(ResourceCommand.Create(r4SchemaProvider));
        r4Command.AddCommand(ScenarioCommand.Create(r4SchemaProvider));
        r4Command.AddCommand(PopulationCommand.Create(r4SchemaProvider));
        rootCommand.AddCommand(r4Command);

        // TODO: Add R5 support when available
        // var r5Command = new Command("r5", "Use FHIR R5 specification");
        // var r5SchemaProvider = new R5CoreSchemaProvider();
        // r5Command.AddCommand(ResourceCommand.Create(r5SchemaProvider));
        // r5Command.AddCommand(ScenarioCommand.Create(r5SchemaProvider));
        // r5Command.AddCommand(PopulationCommand.Create(r5SchemaProvider));
        // rootCommand.AddCommand(r5Command);

        return await rootCommand.InvokeAsync(args);
    }
}
