// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

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

        // Add STU3 support
        var stu3Command = new Command("stu3", "Use FHIR STU3 specification");
        var stu3SchemaProvider = new STU3CoreSchemaProvider();
        stu3Command.AddCommand(ConvertCommand.Create(stu3SchemaProvider, "stu3"));
        stu3Command.AddCommand(PreviewCommand.Create(stu3SchemaProvider, "stu3"));
        stu3Command.AddCommand(ValidateCommand.Create(stu3SchemaProvider, "stu3"));
        rootCommand.AddCommand(stu3Command);

        // Add R4 support
        var r4Command = new Command("r4", "Use FHIR R4 specification");
        var r4SchemaProvider = new R4CoreSchemaProvider();
        r4Command.AddCommand(ConvertCommand.Create(r4SchemaProvider, "r4"));
        r4Command.AddCommand(PreviewCommand.Create(r4SchemaProvider, "r4"));
        r4Command.AddCommand(ValidateCommand.Create(r4SchemaProvider, "r4"));
        rootCommand.AddCommand(r4Command);

        // Add R4B support
        var r4bCommand = new Command("r4b", "Use FHIR R4B specification");
        var r4bSchemaProvider = new R4BCoreSchemaProvider();
        r4bCommand.AddCommand(ConvertCommand.Create(r4bSchemaProvider, "r4b"));
        r4bCommand.AddCommand(PreviewCommand.Create(r4bSchemaProvider, "r4b"));
        r4bCommand.AddCommand(ValidateCommand.Create(r4bSchemaProvider, "r4b"));
        rootCommand.AddCommand(r4bCommand);

        // Add R5 support
        var r5Command = new Command("r5", "Use FHIR R5 specification");
        var r5SchemaProvider = new R5CoreSchemaProvider();
        r5Command.AddCommand(ConvertCommand.Create(r5SchemaProvider, "r5"));
        r5Command.AddCommand(PreviewCommand.Create(r5SchemaProvider, "r5"));
        r5Command.AddCommand(ValidateCommand.Create(r5SchemaProvider, "r5"));
        rootCommand.AddCommand(r5Command);

        // Add R6 support
        var r6Command = new Command("r6", "Use FHIR R6 specification");
        var r6SchemaProvider = new R6CoreSchemaProvider();
        r6Command.AddCommand(ConvertCommand.Create(r6SchemaProvider, "r6"));
        r6Command.AddCommand(PreviewCommand.Create(r6SchemaProvider, "r6"));
        r6Command.AddCommand(ValidateCommand.Create(r6SchemaProvider, "r6"));
        rootCommand.AddCommand(r6Command);

        return await rootCommand.InvokeAsync(args);
    }
}
