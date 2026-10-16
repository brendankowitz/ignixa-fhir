// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.CommandLine;
using Ignixa.CliOnFhir.Session;
using Ignixa.CliOnFhir.Validation;
using Microsoft.Extensions.Logging;

namespace Ignixa.CliOnFhir.Commands;

/// <summary><c>cli-on-fhir validate &lt;path&gt;</c>: the CLI form of the <c>validate_resources</c> MCP tool, with the same arguments and envelope.</summary>
internal static class ValidateCommand
{
    public static Command Create()
    {
        Argument<string> pathArgument = CommonOptions.CreatePathArgument();
        Option<string> versionOption = CommonOptions.CreateFhirVersionOption();
        var typeOption = new Option<string?>("--type") { Description = "Validate resources of this type, for example Observation" };
        var queryOption = new Option<string?>("--query") { Description = "FHIR search query over --type choosing the resources, for example \"code=http://loinc.org|8867-4\"" };
        var patientOption = new Option<string?>("--patient") { Description = "A Patient id: its compartment, or with --type the members of that type" };
        var referenceOption = new Option<string[]>("--reference") { Description = "A resource to validate, Type/id (repeatable); not combined with --type, --query or --patient" };
        var profileOption = new Option<string?>("--profile") { Description = "Validate against this one profile canonical (url or url|version) instead of each resource's meta.profile" };
        var packageOption = new Option<string[]>("--package") { Description = "A FHIR npm package, id@version (repeatable), whose StructureDefinitions and terminology to load first" };
        var depthOption = new Option<string?>("--depth") { Description = "full (default: structure, cardinality, types, bindings, invariants, slicing) or spec (no invariants, slicing, extensible bindings or complex choice variants, so no resource is reported valid)" };
        var limitOption = new Option<int?>("--limit") { Description = $"Resources validated (default {ResourceValidator.DefaultLimit}, maximum {ResourceValidator.MaxLimit}; with --detail {ResourceValidator.DefaultDetailLimit} and {ResourceValidator.MaxDetailLimit})" };
        var offsetOption = new Option<int>("--offset") { Description = "Resources in scope to skip, in scope order" };
        var detailOption = new Option<bool>("--detail") { Description = "Also return each resource's full OperationOutcome (resource content)" };
        Option<string?> cacheDirOption = CommonOptions.CreateCacheDirOption();
        Option<bool> noCacheOption = CommonOptions.CreateNoCacheOption();
        Option<bool> prettyOption = CommonOptions.CreatePrettyOption();
        var terminology = new TerminologyCliOptions();

        var command = new Command("validate", "Validate the folder's resources against FHIR core definitions and loaded profiles, summarized by rule. Exits non-zero, with the problem in 'issues', when the request cannot be answered");
        command.Arguments.Add(pathArgument);
        foreach (Option option in new Option[] { versionOption, typeOption, queryOption, patientOption, referenceOption, profileOption, packageOption, depthOption, limitOption, offsetOption, detailOption, prettyOption })
        {
            command.Options.Add(option);
        }

        CommonOptions.AddCacheOptions(command, cacheDirOption, noCacheOption);
        terminology.AddTo(command);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            using ILoggerFactory loggerFactory = StandardErrorLogging.Create(LogLevel.Warning);
            FolderSession session = CommonOptions.CreateSession(parseResult, pathArgument, versionOption, cacheDirOption, noCacheOption, loggerFactory, terminology.Resolve(parseResult, cacheDirOption));
            var request = new ValidateRequest(
                parseResult.GetValue(typeOption),
                parseResult.GetValue(queryOption),
                parseResult.GetValue(patientOption),
                parseResult.GetValue(referenceOption),
                parseResult.GetValue(profileOption),
                parseResult.GetValue(packageOption),
                parseResult.GetValue(depthOption),
                parseResult.GetValue(limitOption),
                parseResult.GetValue(offsetOption),
                parseResult.GetValue(detailOption));
            return EnvelopeOutput.Write(await ResourceValidator.ValidateAsync(session, request, cancellationToken), parseResult.GetValue(prettyOption));
        });

        return command;
    }
}
