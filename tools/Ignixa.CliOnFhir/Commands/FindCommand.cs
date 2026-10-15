// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.CommandLine;
using Ignixa.CliOnFhir.Patients;
using Ignixa.CliOnFhir.Search;
using Ignixa.CliOnFhir.Session;
using Microsoft.Extensions.Logging;

namespace Ignixa.CliOnFhir.Commands;

/// <summary><c>cli-on-fhir find &lt;path&gt; &lt;patientId&gt; &lt;query&gt;</c>: the CLI form of the <c>context_find</c> MCP tool.</summary>
internal static class FindCommand
{
    public static Command Create()
    {
        Argument<string> pathArgument = CommonOptions.CreatePathArgument();
        var patientArgument = new Argument<string>("patientId") { Description = "The Patient's id" };
        var queryArgument = new Argument<string>("query") { Description = "A code ('http://snomed.info/sct|195967001', '|code' or a bare code) or a text such as 'asthma'" };
        Option<string> versionOption = CommonOptions.CreateFhirVersionOption();
        var countOption = new Option<int?>("--count") { Description = $"Matches per page (default {ResourceSearcher.DefaultCount}, maximum {ResourceSearcher.MaxCount})" };
        var offsetOption = new Option<int>("--offset") { Description = "Matches to skip" };
        var elementsOption = new Option<string?>("--elements") { Description = "Comma-separated top-level elements to keep in the returned resources" };
        Option<string?> cacheDirOption = CommonOptions.CreateCacheDirOption();
        Option<bool> noCacheOption = CommonOptions.CreateNoCacheOption();
        Option<bool> prettyOption = CommonOptions.CreatePrettyOption();

        var command = new Command("find", "Find resources in one patient's context by code, then by text, with the node keys to expand. Exits non-zero, with the problem in 'issues', when the request cannot be answered");
        command.Arguments.Add(pathArgument);
        command.Arguments.Add(patientArgument);
        command.Arguments.Add(queryArgument);
        command.Options.Add(versionOption);
        command.Options.Add(countOption);
        command.Options.Add(offsetOption);
        command.Options.Add(elementsOption);
        command.Options.Add(prettyOption);
        CommonOptions.AddCacheOptions(command, cacheDirOption, noCacheOption);

        command.SetAction((parseResult, cancellationToken) =>
        {
            using ILoggerFactory loggerFactory = StandardErrorLogging.Create(LogLevel.Warning);
            FolderSession session = CommonOptions.CreateSession(parseResult, pathArgument, versionOption, cacheDirOption, noCacheOption, loggerFactory);
            var envelope = ContextFinder.Find(
                session,
                parseResult.GetValue(patientArgument)!,
                parseResult.GetValue(queryArgument)!,
                parseResult.GetValue(countOption),
                parseResult.GetValue(offsetOption),
                parseResult.GetValue(elementsOption),
                cancellationToken);
            return Task.FromResult(EnvelopeOutput.Write(envelope, parseResult.GetValue(prettyOption)));
        });

        return command;
    }
}
