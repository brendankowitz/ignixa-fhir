// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.CommandLine;
using Ignixa.ConformanceMatrix.Runner.Commands;

namespace Ignixa.ConformanceMatrix.Runner;

internal sealed class Program
{
    private static async Task<int> Main(string[] args)
    {
        var root = new RootCommand("ignixa-matrix-runner - FHIR TestScript load-test runner (Azure Load Testing / Locust sidecar)");
        root.Subcommands.Add(ServeCommand.Build());
        return await root.Parse(args).InvokeAsync();
    }
}
