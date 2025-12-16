// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.CommandLine;
using Ignixa.FhirFakes.Cli.Commands;
using Ignixa.Specification;
using Ignixa.Specification.Generated;

namespace Ignixa.FhirFakes.Cli;

/// <summary>
/// Entry point for the FHIR Fakes CLI tool.
/// </summary>
class Program
{
    static async Task<int> Main(string[] args)
    {
        // Create root command
        var rootCommand = new RootCommand("FHIR Fakes - Generate and model FHIR test data");

        // Add help command for discoverability
        rootCommand.Subcommands.Add(HelpCommand.Create());

        // Add version-specific commands
        AddFhirVersionCommands(rootCommand, "stu3", new STU3CoreSchemaProvider());
        AddFhirVersionCommands(rootCommand, "r4", new R4CoreSchemaProvider());
        AddFhirVersionCommands(rootCommand, "r4b", new R4BCoreSchemaProvider());
        AddFhirVersionCommands(rootCommand, "r5", new R5CoreSchemaProvider());
        AddFhirVersionCommands(rootCommand, "r6", new R6CoreSchemaProvider());

        return await rootCommand.Parse(args).InvokeAsync();
    }

    /// <summary>
    /// Adds FHIR version-specific commands to the root command.
    /// Reduces code duplication by centralizing command setup logic.
    /// </summary>
    private static void AddFhirVersionCommands(RootCommand root, string versionCode, IFhirSchemaProvider schemaProvider)
    {
        var command = new Command(versionCode, $"Use FHIR {versionCode.ToUpperInvariant()} specification");
        command.Subcommands.Add(ResourceCommand.Create(schemaProvider, versionCode));
        command.Subcommands.Add(ScenarioCommand.Create(schemaProvider, versionCode));
        command.Subcommands.Add(PopulationCommand.Create(schemaProvider, versionCode));
        root.Subcommands.Add(command);
    }
}
