// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.CommandLine;
using Ignixa.CliOnFhir.Commands;

var root = new RootCommand("cli-on-fhir: explore a local folder of FHIR data from the command line or over MCP (prototype)");
root.Subcommands.Add(InspectCommand.Create());
root.Subcommands.Add(SystemsCommand.Create());
root.Subcommands.Add(SearchCommand.Create());
root.Subcommands.Add(GetCommand.Create());
root.Subcommands.Add(PatientsCommand.Create());
root.Subcommands.Add(ContextCommand.Create());
root.Subcommands.Add(FindCommand.Create());
root.Subcommands.Add(TermCommand.Create());
root.Subcommands.Add(ValidateCommand.Create());
root.Subcommands.Add(McpCommand.Create());

return await root.Parse(args).InvokeAsync();
