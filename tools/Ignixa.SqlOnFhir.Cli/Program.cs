// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.CommandLine;
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

        // Add commands
        rootCommand.AddCommand(ConvertCommand.Create());
        rootCommand.AddCommand(PreviewCommand.Create());
        rootCommand.AddCommand(ValidateCommand.Create());

        return await rootCommand.InvokeAsync(args);
    }
}
