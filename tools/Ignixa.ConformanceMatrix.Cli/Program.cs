// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.CommandLine;
using Ignixa.ConformanceMatrix.Cli.Commands;

namespace Ignixa.ConformanceMatrix.Cli;

internal sealed class Program
{
    private static async Task<int> Main(string[] args)
    {
        var root = new RootCommand("ignixa-matrix - FHIR TestScript conformance matrix runner");
        root.Subcommands.Add(RunCommand.Build());
        root.Subcommands.Add(MergeCommand.Build());
        return await root.Parse(args).InvokeAsync();
    }
}
