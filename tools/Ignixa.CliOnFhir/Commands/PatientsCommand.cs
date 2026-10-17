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

/// <summary><c>cli-on-fhir patients &lt;path&gt;</c>: the CLI form of the <c>patient_list</c> MCP tool.</summary>
internal static class PatientsCommand
{
    public static Command Create()
    {
        Argument<string> pathArgument = CommonOptions.CreatePathArgument();
        Option<string> versionOption = CommonOptions.CreateFhirVersionOption();
        var countOption = new Option<int?>("--count") { Description = $"Patients per page (default {ResourceSearcher.DefaultCount}, maximum {ResourceSearcher.MaxCount})" };
        var offsetOption = new Option<int>("--offset") { Description = "Patients to skip, in sort order" };
        var sortOption = new Option<string?>("--sort") { Description = $"'{PatientLister.ByName}' (default) or '{PatientLister.ByLatest}' (latest clinical date first)" };
        var queryOption = new Option<string?>("--query") { Description = "Only patients whose id, name, identifier value (an MRN) or birthDate contain every whitespace-separated term, ignoring case" };
        Option<string?> cacheDirOption = CommonOptions.CreateCacheDirOption();
        Option<bool> noCacheOption = CommonOptions.CreateNoCacheOption();
        Option<bool> prettyOption = CommonOptions.CreatePrettyOption();

        var command = new Command("patients", "The folder's patients with resource counts, clinical date spans and the domains each one's context holds");
        command.Arguments.Add(pathArgument);
        command.Options.Add(versionOption);
        command.Options.Add(countOption);
        command.Options.Add(offsetOption);
        command.Options.Add(sortOption);
        command.Options.Add(queryOption);
        command.Options.Add(prettyOption);
        CommonOptions.AddCacheOptions(command, cacheDirOption, noCacheOption);

        command.SetAction((parseResult, cancellationToken) =>
        {
            using ILoggerFactory loggerFactory = StandardErrorLogging.Create(LogLevel.Warning);
            FolderSession session = CommonOptions.CreateSession(parseResult, pathArgument, versionOption, cacheDirOption, noCacheOption, loggerFactory);
            var envelope = PatientLister.List(session, parseResult.GetValue(countOption), parseResult.GetValue(offsetOption), parseResult.GetValue(sortOption), parseResult.GetValue(queryOption), cancellationToken);
            return Task.FromResult(EnvelopeOutput.Write(envelope, parseResult.GetValue(prettyOption)));
        });

        return command;
    }
}
