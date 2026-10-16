// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.CommandLine;
using Ignixa.CliOnFhir.CodeSystems;
using Ignixa.CliOnFhir.Contracts;
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
        var topOption = new Option<int?>("--top") { Description = $"Most frequent codes listed per system (none by default; {FolderCodeSystems.DefaultTop} with --detail full; maximum {FolderCodeSystems.MaxTop})" };
        var detailOption = new Option<string?>("--detail") { Description = "'full' adds elements, implicit codes and hints to each system (the default is one compact line per system)" };
        var textOption = new Option<string?>("--text") { Description = "Instead list the codes the folder holds whose display has a word starting with one of these words or alternatives ('diabet|glyc'), with their resource and patient counts; no terminology or network" };
        var resourceTypeOption = new Option<string?>("--resource-type") { Description = "With --text: only codes on this resource type" };
        var systemOption = new Option<string?>("--system") { Description = "With --text: only codes of this system (URI or alias such as sct)" };
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
        command.Options.Add(detailOption);
        command.Options.Add(textOption);
        command.Options.Add(resourceTypeOption);
        command.Options.Add(systemOption);
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
            (int? count, int offset, int? top, bool pretty) = (parseResult.GetValue(countOption), parseResult.GetValue(offsetOption), parseResult.GetValue(topOption), parseResult.GetValue(prettyOption));
            string? text = parseResult.GetValue(textOption);
            string? resourceType = parseResult.GetValue(resourceTypeOption);
            string? system = parseResult.GetValue(systemOption);
            if (text is not null)
            {
                return EnvelopeOutput.Write(FolderCodeSystems.SearchText(session, text, resourceType, system, count, offset, cancellationToken), pretty);
            }

            if (resourceType is not null || system is not null)
            {
                return EnvelopeOutput.Write(EnvelopeAssembler.Failure<object>(session, new IssueItem("error", "invalid-parameter", "--resource-type and --system narrow a --text search; give --text too.", Location: null), cancellationToken), pretty);
            }

            return parseResult.GetValue(detailOption) switch
            {
                "full" => EnvelopeOutput.Write(await FolderCodeSystems.ListAsync(session, count, offset, top, withTerminology, cancellationToken), pretty),
                null or "compact" => EnvelopeOutput.Write(await FolderCodeSystems.ListCompactAsync(session, count, offset, top, withTerminology, cancellationToken), pretty),
                string other => EnvelopeOutput.Write(EnvelopeAssembler.Failure<object>(session, new IssueItem("error", "invalid-parameter", $"--detail is 'compact' or 'full', not '{other}'.", Location: null), cancellationToken), pretty),
            };
        });

        return command;
    }
}
