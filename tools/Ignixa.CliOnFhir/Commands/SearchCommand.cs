// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.CommandLine;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Search;
using Ignixa.CliOnFhir.Session;
using Microsoft.Extensions.Logging;

namespace Ignixa.CliOnFhir.Commands;

/// <summary>
/// <c>cli-on-fhir search &lt;path&gt; &lt;type&gt; [query]</c>: the CLI form of the <c>search_fhir_resources</c> MCP tool, and with
/// <c>--check</c> of <c>plan_fhir_search</c>.
/// </summary>
internal static class SearchCommand
{
    public static Command Create()
    {
        Argument<string> pathArgument = CommonOptions.CreatePathArgument();
        var typeArgument = new Argument<string>("type") { Description = "Resource type to search, for example Observation" };
        var queryArgument = new Argument<string?>("query")
        {
            Description = "FHIR search query string, for example \"code=http://loinc.org|8867-4&_count=5\"; omit to match every resource of the type",
            Arity = ArgumentArity.ZeroOrOne,
        };

        Option<string> versionOption = CommonOptions.CreateFhirVersionOption();
        var countOption = new Option<int?>("--count")
        {
            Description = $"Resources per page (default {ResourceSearcher.DefaultCount}, maximum {ResourceSearcher.MaxCount}); overrides _count in the query",
        };

        var offsetOption = new Option<int>("--offset") { Description = "Matches to skip, in catalog order" };
        var summaryOption = new Option<string?>("--summary") { Description = "'count' returns only the total number of matches" };
        var elementsOption = new Option<string?>("--elements")
        {
            Description = "Comma-separated top-level elements to keep (resourceType, id and meta are always kept), for example code,valueQuantity",
        };

        var lenientOption = new Option<bool>("--lenient")
        {
            Description = "Ignore query parameter names the search does not recognise, with a parameter-ignored warning, instead of failing; this can widen the results",
        };

        var compartmentOption = new Option<string?>("--compartment")
        {
            Description = "Search only within a compartment, for example Patient/123 (as Patient/123/Observation does); membership follows the FHIR version's CompartmentDefinitions",
        };

        var checkOption = new Option<bool>("--check")
        {
            Description = "Do not run the search: report how each parameter resolves (definition, chain targets, modifiers, prefixes), what is ignored or unsupported, and how many resources in the folder populate each parameter, with the top token values",
        };

        var executeOption = new Option<bool>("--execute")
        {
            Description = "With --check, also run the search and return its results beside the plan",
        };

        var columnsOption = new Option<string?>("--columns")
        {
            Description = "Return a table instead of resources: a JSON object mapping each column name to a FHIRPath evaluated on every match, "
                + "for example '{\"date\":\"authoredOn\",\"prescriber\":\"requester.resolve().name.first().family\"}'",
        };

        var fhirPathOption = new Option<string?>("--fhirpath")
        {
            Description = "One FHIRPath evaluated over a searchset Bundle of every match, returned in facts.value, for example 'entry.count()'",
        };

        Option<int?> maxBytesOption = CommonOptions.CreateMaxBytesOption();
        Option<string?> cacheDirOption = CommonOptions.CreateCacheDirOption();
        Option<bool> noCacheOption = CommonOptions.CreateNoCacheOption();
        Option<bool> prettyOption = CommonOptions.CreatePrettyOption();

        var command = new Command("search", "FHIR search over a folder of FHIR data. Exits non-zero, with the problem in 'issues', when the query cannot be answered");
        command.Arguments.Add(pathArgument);
        command.Arguments.Add(typeArgument);
        command.Arguments.Add(queryArgument);
        command.Options.Add(versionOption);
        command.Options.Add(countOption);
        command.Options.Add(offsetOption);
        command.Options.Add(summaryOption);
        command.Options.Add(elementsOption);
        command.Options.Add(lenientOption);
        command.Options.Add(compartmentOption);
        command.Options.Add(checkOption);
        command.Options.Add(executeOption);
        command.Options.Add(columnsOption);
        command.Options.Add(fhirPathOption);
        command.Options.Add(maxBytesOption);
        command.Options.Add(prettyOption);
        command.Validators.Add(result =>
        {
            if (result.GetValue(executeOption) && !result.GetValue(checkOption))
            {
                result.AddError("--execute is only used with --check: a search without --check always runs.");
            }
        });
        CommonOptions.AddCacheOptions(command, cacheDirOption, noCacheOption);

        command.SetAction((parseResult, cancellationToken) =>
        {
            using ILoggerFactory loggerFactory = StandardErrorLogging.Create(LogLevel.Warning);
            FolderSession session = CommonOptions.CreateSession(parseResult, pathArgument, versionOption, cacheDirOption, noCacheOption, loggerFactory);
            IReadOnlyList<SearchColumn>? columns;
            try
            {
                columns = parseResult.GetValue(columnsOption) is { } json ? SearchColumn.FromJson(json) : null;
            }
            catch (ToolRequestException ex)
            {
                return Task.FromResult(EnvelopeOutput.Write(EnvelopeAssembler.Failure<SearchFacts>(session, ex.Issue, cancellationToken), parseResult.GetValue(prettyOption)));
            }

            var request = new SearchRequest(
                parseResult.GetValue(typeArgument)!,
                parseResult.GetValue(queryArgument),
                parseResult.GetValue(countOption),
                parseResult.GetValue(offsetOption),
                parseResult.GetValue(summaryOption),
                parseResult.GetValue(elementsOption),
                parseResult.GetValue(lenientOption),
                parseResult.GetValue(compartmentOption),
                parseResult.GetValue(maxBytesOption),
                columns,
                parseResult.GetValue(fhirPathOption));

            bool pretty = parseResult.GetValue(prettyOption);
            return Task.FromResult(parseResult.GetValue(checkOption)
                ? EnvelopeOutput.Write(SearchPlanner.Plan(session, request, parseResult.GetValue(executeOption), cancellationToken), pretty)
                : EnvelopeOutput.Write(ResourceSearcher.Search(session, request, cancellationToken), pretty));
        });

        return command;
    }
}
