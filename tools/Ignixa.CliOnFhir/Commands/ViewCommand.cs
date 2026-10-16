// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.CommandLine;
using Ignixa.CliOnFhir.Session;
using Ignixa.CliOnFhir.Views;
using Microsoft.Extensions.Logging;

namespace Ignixa.CliOnFhir.Commands;

/// <summary><c>cli-on-fhir view &lt;path&gt; &lt;viewdef&gt;</c>: runs a SQL-on-FHIR ViewDefinition over the folder (the design's <c>sql_view</c>).</summary>
internal static class ViewCommand
{
    public static Command Create()
    {
        Argument<string> pathArgument = CommonOptions.CreatePathArgument();
        var viewArgument = new Argument<string>("viewdef") { Description = "The ViewDefinition: inline JSON, the path of a file, or the id or url of a ViewDefinition in the folder" };
        Option<string> versionOption = CommonOptions.CreateFhirVersionOption();
        var whereOption = new Option<string?>("--where") { Description = "An extra FHIRPath filter, ANDed with the view's own where clauses, for example \"gender = 'female'\"" };
        var varOption = new Option<string[]>("--var") { Description = "A FHIRPath variable name=value, repeatable; it overrides the ViewDefinition constant of that name", AllowMultipleArgumentsPerToken = false };
        var compartmentOption = new Option<string?>("--compartment") { Description = "Only the resources in this Patient compartment, Patient/<id>" };
        var countOption = new Option<int?>("--count") { Description = $"Rows returned (default {ViewPaging.DefaultCount}, maximum {ViewPaging.MaxCount}, 0 for none); the total is always given. Not with --out" };
        var offsetOption = new Option<int>("--offset") { Description = "Rows to skip before the ones returned. Not with --out" };
        var distinctOption = new Option<string?>("--distinct") { Description = "A column whose distinct values are counted over every row, for example the patient column of a Condition view to count patients" };
        var outOption = new Option<string?>("--out") { Description = "Write every row to this file, which must be outside the folder, instead of returning rows" };
        var formatOption = new Option<string?>("--format") { Description = "csv, ndjson or parquet for --out (default: from its extension)" };
        var overwriteOption = new Option<bool>("--overwrite") { Description = "Replace --out when it exists" };
        Option<int?> maxBytesOption = CommonOptions.CreateMaxBytesOption();
        Option<string?> cacheDirOption = CommonOptions.CreateCacheDirOption();
        Option<bool> noCacheOption = CommonOptions.CreateNoCacheOption();
        Option<bool> prettyOption = CommonOptions.CreatePrettyOption();

        var command = new Command("view", "Run a SQL-on-FHIR ViewDefinition over the folder's resources of its type: rows in the response (capped, with the total) or every row to a csv, ndjson or parquet file outside the folder");
        command.Arguments.Add(pathArgument);
        command.Arguments.Add(viewArgument);
        foreach (Option option in new Option[] { versionOption, whereOption, varOption, compartmentOption, countOption, offsetOption, distinctOption, outOption, formatOption, overwriteOption, maxBytesOption, prettyOption })
        {
            command.Options.Add(option);
        }

        CommonOptions.AddCacheOptions(command, cacheDirOption, noCacheOption);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            using ILoggerFactory loggerFactory = StandardErrorLogging.Create(LogLevel.Warning);
            FolderSession session = CommonOptions.CreateSession(parseResult, pathArgument, versionOption, cacheDirOption, noCacheOption, loggerFactory);
            var request = new ViewRequest(
                parseResult.GetValue(viewArgument)!,
                parseResult.GetValue(whereOption),
                parseResult.GetValue(varOption),
                parseResult.GetValue(compartmentOption),
                parseResult.GetValue(countOption),
                parseResult.GetValue(offsetOption),
                parseResult.GetValue(distinctOption),
                parseResult.GetValue(outOption),
                parseResult.GetValue(formatOption),
                parseResult.GetValue(overwriteOption),
                parseResult.GetValue(maxBytesOption));
            return EnvelopeOutput.Write(await ViewRunner.RunAsync(session, request, cancellationToken), parseResult.GetValue(prettyOption));
        });

        return command;
    }
}
