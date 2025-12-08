using System.CommandLine;
using Ignixa.Fml.Cli.Commands;

namespace Ignixa.Fml.Cli;

/// <summary>
/// Entry point for the FHIR Mapping Language CLI tool.
/// </summary>
class Program
{
    static async Task<int> Main(string[] args)
    {
        // Create root command
        var rootCommand = new RootCommand("FHIR Mapping Language (FML) - Transform, preview, and validate FHIR mappings");

        // Add commands
        rootCommand.AddCommand(ConvertCommand.Create());
        rootCommand.AddCommand(PreviewCommand.Create());
        rootCommand.AddCommand(ValidateCommand.Create());

        return await rootCommand.InvokeAsync(args);
    }
}
