// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.CommandLine;
using Ignixa.CliOnFhir.Clock;
using Ignixa.CliOnFhir.Patients;
using Ignixa.CliOnFhir.Session;
using Microsoft.Extensions.Logging;

namespace Ignixa.CliOnFhir.Commands;

/// <summary>
/// <c>cli-on-fhir summary &lt;path&gt; &lt;patient&gt;</c>: the CLI form of the <c>patient_summary</c> MCP tool, the patient map of
/// the Patient named by id, identifier value (an MRN) or <c>system|value</c>. It reads the root <c>--as-of</c>
/// (<see cref="AsOfOption"/>); without it the map is read as of the patient's latest dated resource.
/// </summary>
internal static class SummaryCommand
{
    public static Command Create()
    {
        Argument<string> pathArgument = CommonOptions.CreatePathArgument();
        var patientArgument = new Argument<string>("patient") { Description = "The Patient's id, an identifier value (an MRN) or system|value" };
        Option<string> versionOption = CommonOptions.CreateFhirVersionOption();
        Option<string?> cacheDirOption = CommonOptions.CreateCacheDirOption();
        Option<bool> noCacheOption = CommonOptions.CreateNoCacheOption();
        Option<bool> prettyOption = CommonOptions.CreatePrettyOption();

        var command = new Command("summary", "One patient's map, as the patient_summary MCP tool returns it. Exits non-zero, with the problem in 'issues', when the patient is unknown or the identifier names several");
        command.Arguments.Add(pathArgument);
        command.Arguments.Add(patientArgument);
        command.Options.Add(versionOption);
        command.Options.Add(prettyOption);
        CommonOptions.AddCacheOptions(command, cacheDirOption, noCacheOption);

        command.SetAction((parseResult, cancellationToken) =>
        {
            using ILoggerFactory loggerFactory = StandardErrorLogging.Create(LogLevel.Warning);
            FolderSession session = CommonOptions.CreateSession(parseResult, pathArgument, versionOption, cacheDirOption, noCacheOption, loggerFactory);
            string patient = parseResult.GetValue(patientArgument)!;
            AsOfClock asOf = session.AsOfClock;
            return Task.FromResult(EnvelopeOutput.Write(PatientMap.Summarize(session, patient, asOf, cancellationToken), parseResult.GetValue(prettyOption)));
        });

        return command;
    }
}
