using System.CommandLine;
using Ignixa.Specification;
using Ignixa.Specification.Generated;
using Ignixa.Validation.Cli.Commands;

namespace Ignixa.Validation.Cli;

/// <summary>
/// Entry point for the FHIR Validation CLI tool.
/// </summary>
class Program
{
    static async Task<int> Main(string[] args)
    {
        // Create root command
        var rootCommand = new RootCommand("FHIR Validation - Validate FHIR resources against specifications");

        // Add STU3 support
        var stu3Command = ValidateCommand.Create(new STU3CoreSchemaProvider(), "stu3");
        stu3Command.Name = "stu3";
        stu3Command.Description = "Validate using FHIR STU3 specification";
        rootCommand.AddCommand(stu3Command);

        // Add R4 support
        var r4Command = ValidateCommand.Create(new R4CoreSchemaProvider(), "r4");
        r4Command.Name = "r4";
        r4Command.Description = "Validate using FHIR R4 specification";
        rootCommand.AddCommand(r4Command);

        // Add R4B support
        var r4bCommand = ValidateCommand.Create(new R4BCoreSchemaProvider(), "r4b");
        r4bCommand.Name = "r4b";
        r4bCommand.Description = "Validate using FHIR R4B specification";
        rootCommand.AddCommand(r4bCommand);

        // Add R5 support
        var r5Command = ValidateCommand.Create(new R5CoreSchemaProvider(), "r5");
        r5Command.Name = "r5";
        r5Command.Description = "Validate using FHIR R5 specification";
        rootCommand.AddCommand(r5Command);

        // Add R6 support
        var r6Command = ValidateCommand.Create(new R6CoreSchemaProvider(), "r6");
        r6Command.Name = "r6";
        r6Command.Description = "Validate using FHIR R6 specification";
        rootCommand.AddCommand(r6Command);

        return await rootCommand.InvokeAsync(args);
    }
}
