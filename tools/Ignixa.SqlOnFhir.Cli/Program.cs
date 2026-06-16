using System.CommandLine;
using System.Text;
using Ignixa.Abstractions;
using Ignixa.Specification;
using Ignixa.Specification.Generated;
using Ignixa.SqlOnFhir.Cli.Commands;

namespace Ignixa.SqlOnFhir.Cli;

class Program
{
    static async Task<int> Main(string[] args)
    {
        TryEnableUtf8ConsoleOutput();

        var rootCommand = new RootCommand("SQL on FHIR - Process FHIR resources using ViewDefinitions");

        AddFhirVersionCommands(rootCommand, "stu3", new STU3CoreSchemaProvider());
        AddFhirVersionCommands(rootCommand, "r4",   new R4CoreSchemaProvider());
        AddFhirVersionCommands(rootCommand, "r4b",  new R4BCoreSchemaProvider());
        AddFhirVersionCommands(rootCommand, "r5",   new R5CoreSchemaProvider());
        AddFhirVersionCommands(rootCommand, "r6",   new R6CoreSchemaProvider());

        return await rootCommand.Parse(args).InvokeAsync();
    }

    /// <summary>
    /// Emit UTF-8 so non-ASCII patient names and status glyphs render correctly in terminals,
    /// redirected output, and notebook runners (e.g. runme.dev), which decode captured output as
    /// UTF-8. Best-effort: setting the encoding throws when no console is attached (fully
    /// redirected), so the failure is swallowed and surfaced via the return value.
    /// </summary>
    /// <returns><c>true</c> if the encoding was set; <c>false</c> if no console was available.</returns>
    internal static bool TryEnableUtf8ConsoleOutput()
    {
        try
        {
            Console.OutputEncoding = new UTF8Encoding(false);
            return true;
        }
        catch (Exception)
        {
            // No console attached or the platform doesn't allow changing the encoding.
            return false;
        }
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
