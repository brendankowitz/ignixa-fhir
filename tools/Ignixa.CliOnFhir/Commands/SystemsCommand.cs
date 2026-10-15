// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.CommandLine;
using Ignixa.CliOnFhir.CodeSystems;
using Ignixa.CliOnFhir.Session;
using Microsoft.Extensions.Logging;

namespace Ignixa.CliOnFhir.Commands;

/// <summary><c>cli-on-fhir systems &lt;path&gt;</c>: the CLI form of the <c>folder_code_systems</c> MCP tool.</summary>
internal static class SystemsCommand
{
    public static Command Create()
    {
        Argument<string> pathArgument = CommonOptions.CreatePathArgument();
        Option<string> versionOption = CommonOptions.CreateFhirVersionOption();
        var countOption = new Option<int?>("--count") { Description = "Systems per page (default 10, maximum 50)" };
        var offsetOption = new Option<int>("--offset") { Description = "Systems to skip, in usage order" };
        var topOption = new Option<int?>("--top") { Description = $"Most frequent codes listed per system (default {FolderCodeSystems.DefaultTop}, maximum {FolderCodeSystems.MaxTop})" };
        var withTerminologyOption = new Option<bool>("--with-terminology")
        {
            Description = "Load terminology first (the --terminology packages, else the core package; this may download it unless --offline) and report how each system's codes resolve. Implied by --terminology",
        };

        Option<string?> cacheDirOption = CommonOptions.CreateCacheDirOption();
        Option<bool> noCacheOption = CommonOptions.CreateNoCacheOption();
        Option<bool> prettyOption = CommonOptions.CreatePrettyOption();
        var terminology = new TerminologyCliOptions();

        var command = new Command("systems", "Which coding systems the folder's data uses (SNOMED CT, LOINC, ...), how much and where; no terminology or network needed");
        command.Arguments.Add(pathArgument);
        command.Options.Add(versionOption);
        command.Options.Add(countOption);
        command.Options.Add(offsetOption);
        command.Options.Add(topOption);
        command.Options.Add(withTerminologyOption);
        command.Options.Add(prettyOption);
        CommonOptions.AddCacheOptions(command, cacheDirOption, noCacheOption);
        terminology.AddTo(command);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            using ILoggerFactory loggerFactory = StandardErrorLogging.Create(LogLevel.Warning);
            FolderSession session = CommonOptions.CreateSession(parseResult, pathArgument, versionOption, cacheDirOption, noCacheOption, loggerFactory, terminology.Resolve(parseResult, cacheDirOption));

            // Naming packages on the command line asks for them: the CLI process has no earlier terminology call.
            bool withTerminology = parseResult.GetValue(withTerminologyOption) || (parseResult.GetValue(terminology.Packages)?.Length ?? 0) > 0;
            return EnvelopeOutput.Write(
                await FolderCodeSystems.ListAsync(session, parseResult.GetValue(countOption), parseResult.GetValue(offsetOption), parseResult.GetValue(topOption), withTerminology, cancellationToken),
                parseResult.GetValue(prettyOption));
        });

        return command;
    }
}
