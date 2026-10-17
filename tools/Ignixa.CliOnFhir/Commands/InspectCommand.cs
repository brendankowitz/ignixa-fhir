// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.CommandLine;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Inspection;
using Ignixa.CliOnFhir.Session;
using Microsoft.Extensions.Logging;

namespace Ignixa.CliOnFhir.Commands;

/// <summary><c>cli-on-fhir inspect &lt;path&gt;</c>: the CLI form of the <c>folder_inspect</c> MCP tool.</summary>
internal static class InspectCommand
{
    public static Command Create()
    {
        Argument<string> pathArgument = CommonOptions.CreatePathArgument();
        Option<string> versionOption = CommonOptions.CreateFhirVersionOption();
        var issueLimitOption = new Option<int>("--issue-limit")
        {
            Description = "Maximum number of individual problems to list",
            DefaultValueFactory = _ => FolderInspector.DefaultIssueLimit,
        };

        var buildIndexOption = new Option<bool>("--build-index")
        {
            Description = "Also build the search index for every resource type and report entry counts and build times "
                + "(measurement aid; inspect itself answers from the catalog without indexing)",
        };

        var withTerminologyOption = new Option<bool>("--with-terminology")
        {
            Description = "Load the terminology store first (the --terminology packages, else the core package; this may download it unless --offline) "
                + "and report per code system how many of the folder's codes resolve, are inactive or unknown (builds every type's search index)",
        };

        Option<string?> cacheDirOption = CommonOptions.CreateCacheDirOption();
        Option<bool> noCacheOption = CommonOptions.CreateNoCacheOption();
        Option<bool> prettyOption = CommonOptions.CreatePrettyOption();
        var terminology = new TerminologyCliOptions();

        var command = new Command("inspect", "Overview of a folder of FHIR data: resource types, counts and problems found");
        command.Arguments.Add(pathArgument);
        command.Options.Add(versionOption);
        command.Options.Add(issueLimitOption);
        command.Options.Add(buildIndexOption);
        command.Options.Add(withTerminologyOption);
        command.Options.Add(prettyOption);
        CommonOptions.AddCacheOptions(command, cacheDirOption, noCacheOption);
        terminology.AddTo(command);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            using ILoggerFactory loggerFactory = StandardErrorLogging.Create(LogLevel.Warning);
            FolderSession session = CommonOptions.CreateSession(parseResult, pathArgument, versionOption, cacheDirOption, noCacheOption, loggerFactory, terminology.Resolve(parseResult, cacheDirOption));

            ToolEnvelope<FolderInspection> envelope = await FolderInspector.InspectAsync(session, parseResult.GetValue(issueLimitOption), parseResult.GetValue(buildIndexOption), parseResult.GetValue(withTerminologyOption), cancellationToken);
            return EnvelopeOutput.Write(envelope, parseResult.GetValue(prettyOption));
        });

        return command;
    }
}
