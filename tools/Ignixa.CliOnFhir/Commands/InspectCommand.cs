// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.CommandLine;
using System.Text.Json;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Inspection;
using Ignixa.CliOnFhir.Session;
using Microsoft.Extensions.Logging;

namespace Ignixa.CliOnFhir.Commands;

/// <summary><c>cli-on-fhir inspect &lt;path&gt;</c>: the CLI form of the <c>folder_inspect</c> MCP tool.</summary>
internal static class InspectCommand
{
    private static readonly JsonSerializerOptions OutputOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

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

        Option<string?> cacheDirOption = CommonOptions.CreateCacheDirOption();
        Option<bool> noCacheOption = CommonOptions.CreateNoCacheOption();

        var command = new Command("inspect", "Overview of a folder of FHIR data: resource types, counts and problems found");
        command.Arguments.Add(pathArgument);
        command.Options.Add(versionOption);
        command.Options.Add(issueLimitOption);
        command.Options.Add(buildIndexOption);
        CommonOptions.AddCacheOptions(command, cacheDirOption, noCacheOption);

        command.SetAction(parseResult =>
        {
            using ILoggerFactory loggerFactory = StandardErrorLogging.Create(LogLevel.Warning);
            var session = new FolderSession(
                parseResult.GetValue(pathArgument)!,
                FhirSchemas.Resolve(parseResult.GetValue(versionOption)!),
                loggerFactory,
                CommonOptions.ResolveCache(parseResult, cacheDirOption, noCacheOption));

            ToolEnvelope<FolderInspection> envelope = FolderInspector.Inspect(session, parseResult.GetValue(issueLimitOption), parseResult.GetValue(buildIndexOption));
            Console.Out.WriteLine(JsonSerializer.Serialize(envelope, OutputOptions));
            return 0;
        });

        return command;
    }
}
