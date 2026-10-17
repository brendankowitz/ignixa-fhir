// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.CommandLine;
using Ignixa.CliOnFhir.Clock;
using Ignixa.CliOnFhir.Drafting;
using Ignixa.CliOnFhir.Session;
using Microsoft.Extensions.Logging;

namespace Ignixa.CliOnFhir.Commands;

/// <summary><c>cli-on-fhir draft &lt;path&gt; &lt;type&gt; --input &lt;json|file&gt;</c>: a checked draft MedicationRequest, ServiceRequest or Observation (the design's <c>draft_resource</c>).</summary>
internal static class DraftCommand
{
    public static Command Create()
    {
        Argument<string> pathArgument = CommonOptions.CreatePathArgument();
        var typeArgument = new Argument<string>("type") { Description = $"The resource to draft: {string.Join(", ", DraftBuilder.ResourceTypes)}" };
        Option<string> versionOption = CommonOptions.CreateFhirVersionOption();
        var inputOption = new Option<string>("--input") { Description = "The draft's members as inline JSON or a file (not raw FHIR; see the README's draft section), for example {\"patient\":\"Patient/123\",\"code\":{\"system\":\"http://loinc.org\",\"code\":\"2601-3\"}}", Required = true };
        var profileOption = new Option<string?>("--profile") { Description = "A profile canonical (url or url|version) the draft claims and is validated against; it must be loaded, from the folder or --package" };
        var packageOption = new Option<string[]>("--package") { Description = "A FHIR npm package, id@version (repeatable), whose StructureDefinitions and terminology to load first, for example hl7.fhir.us.core@6.1.0" };
        var outOption = new Option<string?>("--out") { Description = "Also write the draft to <Type>-<id>.json in this directory, which must be outside the folder (not when the draft is invalid)" };
        var overwriteOption = new Option<bool>("--overwrite") { Description = "Replace that file when it exists" };
        var timeoutOption = new Option<int?>("--timeout") { Description = $"Seconds the draft may take (default {DraftService.DefaultTimeoutSeconds}, at most {DraftService.MaxTimeoutSeconds}); it is then stopped, and nothing is written" };
        Option<string?> cacheDirOption = CommonOptions.CreateCacheDirOption();
        Option<bool> noCacheOption = CommonOptions.CreateNoCacheOption();
        Option<bool> prettyOption = CommonOptions.CreatePrettyOption();
        var terminology = new TerminologyCliOptions();

        var command = new Command("draft", "Draft a MedicationRequest, ServiceRequest or Observation for a patient of the folder from a few typed members, and check it: validator (core and --profile), every code against its code system, every reference against the folder. The draft is returned (and with --out written outside the folder); nothing is written to the folder");
        command.Arguments.Add(pathArgument);
        command.Arguments.Add(typeArgument);
        foreach (Option option in new Option[] { versionOption, inputOption, profileOption, packageOption, outOption, overwriteOption, timeoutOption, prettyOption })
        {
            command.Options.Add(option);
        }

        CommonOptions.AddCacheOptions(command, cacheDirOption, noCacheOption);
        terminology.AddTo(command);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            using ILoggerFactory loggerFactory = StandardErrorLogging.Create(LogLevel.Warning);
            FolderSession session = CommonOptions.CreateSession(parseResult, pathArgument, versionOption, cacheDirOption, noCacheOption, loggerFactory, terminology.Resolve(parseResult, cacheDirOption));
            var request = new DraftRequest(
                parseResult.GetValue(typeArgument)!,
                parseResult.GetValue(inputOption)!,
                parseResult.GetValue(profileOption),
                parseResult.GetValue(packageOption),
                parseResult.GetValue(outOption),
                parseResult.GetValue(overwriteOption),
                parseResult.GetValue(timeoutOption));
            ILogger logger = loggerFactory.CreateLogger("draft");
            AsOfClock asOf = session.AsOfClock;
            return await Watchdog.RunAsync<DraftFacts>("draft-timeout", request.TimeoutSeconds, DraftService.DefaultTimeoutSeconds, DraftService.MaxTimeoutSeconds, token => DraftService.RunAsync(session, request, asOf, logger, token), parseResult.GetValue(prettyOption), session.AsOfClock, cancellationToken);
        });

        return command;
    }
}
