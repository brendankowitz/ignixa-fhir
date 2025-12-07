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
        var rootCommand = new RootCommand("FHIR Faker - Generate and model FHIR test data");

        // Add help command for discoverability
        rootCommand.AddCommand(HelpCommand.Create());

        // Add STU3 support
        var stu3Command = new Command("stu3", "Use FHIR STU3 specification");
        var stu3SchemaProvider = new STU3CoreSchemaProvider();
        stu3Command.AddCommand(ResourceCommand.Create(stu3SchemaProvider, "stu3"));
        stu3Command.AddCommand(ScenarioCommand.Create(stu3SchemaProvider, "stu3"));
        stu3Command.AddCommand(PopulationCommand.Create(stu3SchemaProvider, "stu3"));
        rootCommand.AddCommand(stu3Command);

        // Add R4 support
        var r4Command = new Command("r4", "Use FHIR R4 specification");
        var r4SchemaProvider = new R4CoreSchemaProvider();
        r4Command.AddCommand(ResourceCommand.Create(r4SchemaProvider, "r4"));
        r4Command.AddCommand(ScenarioCommand.Create(r4SchemaProvider, "r4"));
        r4Command.AddCommand(PopulationCommand.Create(r4SchemaProvider, "r4"));
        rootCommand.AddCommand(r4Command);

        // Add R4B support
        var r4bCommand = new Command("r4b", "Use FHIR R4B specification");
        var r4bSchemaProvider = new R4BCoreSchemaProvider();
        r4bCommand.AddCommand(ResourceCommand.Create(r4bSchemaProvider, "r4b"));
        r4bCommand.AddCommand(ScenarioCommand.Create(r4bSchemaProvider, "r4b"));
        r4bCommand.AddCommand(PopulationCommand.Create(r4bSchemaProvider, "r4b"));
        rootCommand.AddCommand(r4bCommand);

        // Add R5 support
        var r5Command = new Command("r5", "Use FHIR R5 specification");
        var r5SchemaProvider = new R5CoreSchemaProvider();
        r5Command.AddCommand(ResourceCommand.Create(r5SchemaProvider, "r5"));
        r5Command.AddCommand(ScenarioCommand.Create(r5SchemaProvider, "r5"));
        r5Command.AddCommand(PopulationCommand.Create(r5SchemaProvider, "r5"));
        rootCommand.AddCommand(r5Command);

        // Add R6 support
        var r6Command = new Command("r6", "Use FHIR R6 specification");
        var r6SchemaProvider = new R6CoreSchemaProvider();
        r6Command.AddCommand(ResourceCommand.Create(r6SchemaProvider, "r6"));
        r6Command.AddCommand(ScenarioCommand.Create(r6SchemaProvider, "r6"));
        r6Command.AddCommand(PopulationCommand.Create(r6SchemaProvider, "r6"));
        rootCommand.AddCommand(r6Command);

        return await rootCommand.InvokeAsync(args);
    }
}
