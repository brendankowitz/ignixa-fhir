// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.CommandLine;
using Ignixa.CliOnFhir.DeId;
using Ignixa.CliOnFhir.Session;
using Microsoft.Extensions.Logging;

namespace Ignixa.CliOnFhir.Commands;

/// <summary><c>cli-on-fhir deid &lt;path&gt; --config &lt;file&gt; --out &lt;dir&gt;</c>: de-identifies resources into NDJSON files outside the folder (the design's <c>deid_resources</c>).</summary>
internal static class DeIdCommand
{
    public static Command Create()
    {
        Argument<string> pathArgument = CommonOptions.CreatePathArgument();
        Option<string> versionOption = CommonOptions.CreateFhirVersionOption();
        var configOption = new Option<string?>("--config") { Description = "The Ignixa.DeId configuration: a file, or inline JSON. Required: there is no safe default profile" };
        var outOption = new Option<string?>("--out") { Description = "Directory to write one <Type>.ndjson file per resource type into; it must be outside the folder" };
        var typeOption = new Option<string?>("--type") { Description = "De-identify resources of this type, for example Observation" };
        var queryOption = new Option<string?>("--query") { Description = "FHIR search query over --type choosing the resources, for example \"code=http://loinc.org|8867-4\"" };
        var patientOption = new Option<string?>("--patient") { Description = "A Patient id: its compartment, or with --type the members of that type" };
        var referenceOption = new Option<string[]>("--reference") { Description = "A resource to de-identify, Type/id (repeatable); not combined with --type, --query or --patient" };
        var overwriteOption = new Option<bool>("--overwrite") { Description = "Replace the <Type>.ndjson files the run writes when they exist" };
        var timeoutOption = new Option<int?>("--timeout") { Description = $"Seconds the run may take (default {DeIdRunner.DefaultTimeoutSeconds}, at most {DeIdRunner.MaxTimeoutSeconds}); it is then stopped, and nothing is written" };
        var maxResourcesOption = new Option<int?>("--max-resources") { Description = $"Most resources the run may read (default {DeIdRunner.DefaultMaxResources}); more fails before any is read" };
        Option<string?> cacheDirOption = CommonOptions.CreateCacheDirOption();
        Option<bool> noCacheOption = CommonOptions.CreateNoCacheOption();
        Option<bool> prettyOption = CommonOptions.CreatePrettyOption();

        var command = new Command("deid", "De-identify the folder's resources (all, a type, a search, a patient's compartment, or references) with an Ignixa.DeId configuration into NDJSON files, one per type, in a directory outside the folder; the response holds counts, never values");
        command.Arguments.Add(pathArgument);
        foreach (Option option in new Option[] { versionOption, configOption, outOption, typeOption, queryOption, patientOption, referenceOption, overwriteOption, timeoutOption, maxResourcesOption, prettyOption })
        {
            command.Options.Add(option);
        }

        CommonOptions.AddCacheOptions(command, cacheDirOption, noCacheOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            using ILoggerFactory loggerFactory = StandardErrorLogging.Create(LogLevel.Warning);
            FolderSession session = CommonOptions.CreateSession(parseResult, pathArgument, versionOption, cacheDirOption, noCacheOption, loggerFactory);
            var request = new DeIdRequest(
                parseResult.GetValue(configOption),
                parseResult.GetValue(outOption),
                parseResult.GetValue(typeOption),
                parseResult.GetValue(queryOption),
                parseResult.GetValue(patientOption),
                parseResult.GetValue(referenceOption),
                parseResult.GetValue(overwriteOption),
                parseResult.GetValue(timeoutOption),
                parseResult.GetValue(maxResourcesOption));
            ILogger logger = loggerFactory.CreateLogger("deid");
            return await Watchdog.RunAsync<DeIdFacts>("deid-timeout", request.TimeoutSeconds, DeIdRunner.DefaultTimeoutSeconds, DeIdRunner.MaxTimeoutSeconds, token => DeIdRunner.RunAsync(session, request, logger, token), parseResult.GetValue(prettyOption), session.AsOfClock, cancellationToken);
        });

        return command;
    }
}
