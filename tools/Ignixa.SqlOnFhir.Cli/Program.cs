using System.CommandLine;
using Ignixa.Abstractions;
using Ignixa.Specification;
using Ignixa.Specification.Generated;
using Ignixa.SqlOnFhir.Cli.Commands;

namespace Ignixa.SqlOnFhir.Cli;

class Program
{
    static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("SQL on FHIR - Process FHIR resources using ViewDefinitions");

        AddFhirVersionCommands(rootCommand, "stu3", new STU3CoreSchemaProvider());
        AddFhirVersionCommands(rootCommand, "r4",   new R4CoreSchemaProvider());
        AddFhirVersionCommands(rootCommand, "r4b",  new R4BCoreSchemaProvider());
        AddFhirVersionCommands(rootCommand, "r5",   new R5CoreSchemaProvider());
        AddFhirVersionCommands(rootCommand, "r6",   new R6CoreSchemaProvider());

        return await rootCommand.Parse(args).InvokeAsync();
    }

    private static void AddFhirVersionCommands(RootCommand root, string versionCode, IFhirSchemaProvider schemaProvider)
    {
        var command = new Command(versionCode, $"Use FHIR {versionCode.ToUpperInvariant()} specification");
        command.Subcommands.Add(RunCommand.Create(schemaProvider, versionCode));
        command.Subcommands.Add(PreviewCommand.Create(schemaProvider, versionCode));
        command.Subcommands.Add(ValidateCommand.Create(schemaProvider, versionCode));
        root.Subcommands.Add(command);
    }
}
