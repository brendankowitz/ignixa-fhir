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

/// <summary>
/// <c>cli-on-fhir context &lt;path&gt; &lt;patientId&gt; [--compact | --node &lt;key&gt;]</c>: the CLI form of the
/// <c>patient_context</c> MCP tool's root operation (<c>detail=full</c>; with <c>--compact</c> the default patient map), and of
/// its expand operation with <c>--node</c>.
/// </summary>
internal static class ContextCommand
{
    public static Command Create()
    {
        Argument<string> pathArgument = CommonOptions.CreatePathArgument();
        var patientArgument = new Argument<string>("patientId") { Description = "The Patient's id, for example one listed by the patients verb" };
        Option<string> versionOption = CommonOptions.CreateFhirVersionOption();
        var nodeOption = new Option<string?>("--node")
        {
            Description = "Expand one node instead of returning the root digest: problems, medications, allergies, encounters, labs, problem/Condition-<id>, encounter/Encounter-<id> or labs/<system>|<code>",
        };
        var compactOption = new Option<bool>("--compact")
        {
            Description = "Print the patient map, exactly what the patient_summary MCP tool returns, instead of the full root digest",
        };
        var countOption = new Option<int?>("--count") { Description = $"With --node: items per page (default {ResourceSearcher.DefaultCount}, maximum {ResourceSearcher.MaxCount})" };
        var offsetOption = new Option<int>("--offset") { Description = "With --node: items to skip" };
        var elementsOption = new Option<string?>("--elements") { Description = "With --node: comma-separated top-level elements to keep in the returned resources" };
        Option<int?> maxBytesOption = CommonOptions.CreateMaxBytesOption();
        Option<string?> cacheDirOption = CommonOptions.CreateCacheDirOption();
        Option<bool> noCacheOption = CommonOptions.CreateNoCacheOption();
        Option<bool> prettyOption = CommonOptions.CreatePrettyOption();

        var command = new Command("context", "One patient's context: the root digest (one stub per domain), or one node of it with --node. Exits non-zero, with the problem in 'issues', when the request cannot be answered");
        command.Arguments.Add(pathArgument);
        command.Arguments.Add(patientArgument);
        command.Options.Add(versionOption);
        command.Options.Add(nodeOption);
        command.Options.Add(compactOption);
        command.Options.Add(countOption);
        command.Options.Add(offsetOption);
        command.Options.Add(elementsOption);
        command.Options.Add(maxBytesOption);
        command.Options.Add(prettyOption);
        CommonOptions.AddCacheOptions(command, cacheDirOption, noCacheOption);
        command.Validators.Add(result =>
        {
            bool paged = result.GetValue(countOption) is not null || result.GetValue(offsetOption) != 0 || result.GetValue(elementsOption) is not null || result.GetValue(maxBytesOption) is not null;
            if (paged && result.GetValue(nodeOption) is null)
            {
                result.AddError("--count, --offset, --elements and --max-bytes apply only with --node; the root digest is not paged.");
            }

            if (result.GetValue(compactOption) && result.GetValue(nodeOption) is not null)
            {
                result.AddError("--compact prints the patient map, the root; it does not apply with --node.");
            }
        });

        command.SetAction((parseResult, cancellationToken) =>
        {
            using ILoggerFactory loggerFactory = StandardErrorLogging.Create(LogLevel.Warning);
            FolderSession session = CommonOptions.CreateSession(parseResult, pathArgument, versionOption, cacheDirOption, noCacheOption, loggerFactory);
            string patientId = parseResult.GetValue(patientArgument)!;
            bool pretty = parseResult.GetValue(prettyOption);
            int exitCode = parseResult.GetValue(nodeOption) is { } nodeKey
                ? EnvelopeOutput.Write(ContextExpander.Expand(session, patientId, nodeKey, parseResult.GetValue(countOption), parseResult.GetValue(offsetOption), parseResult.GetValue(elementsOption), parseResult.GetValue(maxBytesOption), cancellationToken), pretty)
                : parseResult.GetValue(compactOption)
                    ? EnvelopeOutput.Write(PatientMap.Get(session, patientId, cancellationToken), pretty)
                    : EnvelopeOutput.Write(ContextRoot.Get(session, patientId, cancellationToken), pretty);
            return Task.FromResult(exitCode);
        });

        return command;
    }
}
