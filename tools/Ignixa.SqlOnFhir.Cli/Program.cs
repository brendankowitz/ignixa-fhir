// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using System.CommandLine;
using Ignixa.Specification;
using Ignixa.Specification.Generated;
using Ignixa.SqlOnFhir.Cli.Commands;

namespace Ignixa.SqlOnFhir.Cli;

/// <summary>
/// Entry point for the SQL on FHIR CLI tool.
/// </summary>
class Program
{
    static async Task<int> Main(string[] args)
    {
        // Create root command
        var rootCommand = new RootCommand("SQL on FHIR - Convert FHIR resources using ViewDefinitions");

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
        command.Subcommands.Add(ConvertCommand.Create(schemaProvider, versionCode));
        command.Subcommands.Add(PreviewCommand.Create(schemaProvider, versionCode));
        command.Subcommands.Add(ValidateCommand.Create(schemaProvider, versionCode));
        root.Subcommands.Add(command);
    }
}
