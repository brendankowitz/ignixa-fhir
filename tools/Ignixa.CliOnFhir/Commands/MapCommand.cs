// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.CommandLine;
using Ignixa.CliOnFhir.Mapping;
using Ignixa.CliOnFhir.Session;
using Microsoft.Extensions.Logging;

namespace Ignixa.CliOnFhir.Commands;

/// <summary><c>cli-on-fhir map &lt;path&gt; --map &lt;file|url&gt; [--out &lt;dir&gt;]</c>: runs a FHIR Mapping Language map over the folder's resources (the design's <c>fml_transform</c>).</summary>
internal static class MapCommand
{
    public static Command Create()
    {
        Argument<string> pathArgument = CommonOptions.CreatePathArgument();
        Option<string> versionOption = CommonOptions.CreateFhirVersionOption();
        var mapOption = new Option<string>("--map") { Description = "The map: an FML or StructureMap JSON file, inline FML or JSON, or the id or url of a StructureMap in the folder (never fetched)", Required = true };
        var importOption = new Option<string[]>("--import") { Description = "A map the map imports, given as --map is (repeatable); the folder's StructureMaps are searched too" };
        var outOption = new Option<string?>("--out") { Description = "Write every transformed resource to <Type>.ndjson in this directory, outside the folder, instead of returning a preview" };
        var typeOption = new Option<string?>("--type") { Description = "Transform resources of this type (default: the map's source type)" };
        var queryOption = new Option<string?>("--query") { Description = "FHIR search query over --type choosing the resources" };
        var patientOption = new Option<string?>("--patient") { Description = "A Patient id: its compartment, or with --type the members of that type" };
        var referenceOption = new Option<string[]>("--reference") { Description = "A resource to transform, Type/id (repeatable); not combined with --type, --query or --patient" };
        var overwriteOption = new Option<bool>("--overwrite") { Description = "Replace the <Type>.ndjson file --out would get when it exists" };
        var timeoutOption = new Option<int?>("--timeout") { Description = $"Seconds the run may take (default {MapRunner.DefaultTimeoutSeconds}, at most {MapRunner.MaxTimeoutSeconds}); it is then stopped, and nothing is written" };
        var maxResourcesOption = new Option<int?>("--max-resources") { Description = $"Most resources the run may read (default {MapRunner.DefaultMaxResources}); more fails before any is read" };
        Option<int?> maxBytesOption = CommonOptions.CreateMaxBytesOption();
        Option<string?> cacheDirOption = CommonOptions.CreateCacheDirOption();
        Option<bool> noCacheOption = CommonOptions.CreateNoCacheOption();
        Option<bool> prettyOption = CommonOptions.CreatePrettyOption();

        var command = new Command("map", "Run a FHIR Mapping Language map (FML or StructureMap) over the folder's resources of its source type: a preview of the first transformed resources, or every one written to NDJSON files outside the folder");
        command.Arguments.Add(pathArgument);
        foreach (Option option in new Option[] { versionOption, mapOption, importOption, outOption, typeOption, queryOption, patientOption, referenceOption, overwriteOption, timeoutOption, maxResourcesOption, maxBytesOption, prettyOption })
        {
            command.Options.Add(option);
        }

        CommonOptions.AddCacheOptions(command, cacheDirOption, noCacheOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            using ILoggerFactory loggerFactory = StandardErrorLogging.Create(LogLevel.Warning);
            FolderSession session = CommonOptions.CreateSession(parseResult, pathArgument, versionOption, cacheDirOption, noCacheOption, loggerFactory);
            var request = new MapRequest(
                parseResult.GetValue(mapOption)!,
                parseResult.GetValue(importOption),
                parseResult.GetValue(outOption),
                parseResult.GetValue(typeOption),
                parseResult.GetValue(queryOption),
                parseResult.GetValue(patientOption),
                parseResult.GetValue(referenceOption),
                parseResult.GetValue(overwriteOption),
                parseResult.GetValue(maxBytesOption),
                parseResult.GetValue(timeoutOption),
                parseResult.GetValue(maxResourcesOption));
            ILogger logger = loggerFactory.CreateLogger("map");
            return await Watchdog.RunAsync<MapFacts>("map-timeout", request.TimeoutSeconds, MapRunner.DefaultTimeoutSeconds, MapRunner.MaxTimeoutSeconds, token => MapRunner.RunAsync(session, request, logger, token), parseResult.GetValue(prettyOption), cancellationToken);
        });

        return command;
    }
}
