// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.CommandLine;
using Ignixa.Specification.Generated;

namespace Ignixa.DeId.Cli;

/// <summary>
/// Entry point for the FHIR DeId CLI tool.
/// </summary>
class Program
{
    static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("FHIR data de-identification tool supporting multiple FHIR versions");

        AddFhirVersionCommands(rootCommand, "stu3", new STU3CoreSchemaProvider());
        AddFhirVersionCommands(rootCommand, "r4", new R4CoreSchemaProvider());
        AddFhirVersionCommands(rootCommand, "r4b", new R4BCoreSchemaProvider());
        AddFhirVersionCommands(rootCommand, "r5", new R5CoreSchemaProvider());
        AddFhirVersionCommands(rootCommand, "r6", new R6CoreSchemaProvider());

        return await rootCommand.Parse(args).InvokeAsync();
    }

    private static void AddFhirVersionCommands(RootCommand root, string versionCode, Ignixa.Abstractions.IFhirSchemaProvider schema)
    {
        var command = new Command(versionCode, $"Deidentify FHIR {versionCode.ToUpperInvariant()} resources");
        command.Subcommands.Add(DeIdCommand.Create(schema));
        root.Subcommands.Add(command);
    }
}
