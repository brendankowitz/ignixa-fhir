// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.CommandLine;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Session;
using Ignixa.CliOnFhir.Terminology.DrugClasses;
using Ignixa.CliOnFhir.Terminology.Tools;
using Microsoft.Extensions.Logging;

namespace Ignixa.CliOnFhir.Commands;

/// <summary>
/// <c>cli-on-fhir term &lt;verb&gt; &lt;path&gt; ...</c>: the CLI forms of the terminology MCP tools, with the same arguments
/// and envelope. Each verb opens the folder (whose own CodeSystems, ValueSets and ConceptMaps load first) and the configured
/// packages, or the core package when none is configured. Exits non-zero, with the problem in 'issues', when the call fails.
/// </summary>
internal static class TermCommand
{
    public static Command Create()
    {
        var command = new Command("term", "Terminology: look up, search, validate, navigate, expand and translate codes from FHIR npm packages and the folder; drug classes from RxClass");
        command.Subcommands.Add(List());
        command.Subcommands.Add(Lookup());
        command.Subcommands.Add(Search());
        command.Subcommands.Add(Validate());
        command.Subcommands.Add(Subsumes());
        command.Subcommands.Add(Tree());
        command.Subcommands.Add(Expand());
        command.Subcommands.Add(Translate());
        command.Subcommands.Add(Load());
        command.Subcommands.Add(ClassSearch());
        command.Subcommands.Add(ClassMembers());
        command.Subcommands.Add(Classes());
        return command;
    }

    private static Command List()
    {
        var kind = new Option<string?>("--kind") { Description = "codeSystems (default), valueSets or conceptMaps" };
        var package = new Option<string?>("--package") { Description = "Only resources of this source: id@version or folder" };
        var filter = new Option<string?>("--filter") { Description = "Case-insensitive substring of the canonical url" };
        (Option<int?> count, Option<int> offset) = Paging();
        var usage = new Option<bool>("--folder-usage") { Description = "Also report how the folder's codes resolve, per system (builds the folder's search indexes)" };
        return Verb("list", "Which terminology is loaded and from where", [], [kind, package, filter, count, offset, usage], (parse, session, token) =>
            TermList.ListAsync(session, parse.GetValue(kind), parse.GetValue(package), parse.GetValue(filter), parse.GetValue(count), parse.GetValue(offset), parse.GetValue(usage), token));
    }

    private static Command Lookup()
    {
        Argument<string> system = Required("system", "The code system url, for example http://hl7.org/fhir/administrative-gender");
        Argument<string> code = Required("code", "The code, for example female");
        Option<string?> version = Version();
        return Verb("lookup", "Display, definition, status, properties, designations and parents of one code", [system, code], [version], (parse, session, token) =>
            TermLookup.LookupAsync(session, parse.GetValue(system), parse.GetValue(code), parse.GetValue(version), token));
    }

    private static Command Search()
    {
        Argument<string> text = Required("text", "The text to find over displays and designations, for example inpatient");
        var system = new Option<string?>("--system") { Description = "Only this code system url" };
        var valueSet = new Option<string?>("--value-set") { Description = "Only the codes of this ValueSet (url or url|version)" };
        (Option<int?> count, Option<int> offset) = Paging();
        return Verb("search", "Text to candidate codes, ranked exact > prefix > contains", [text], [system, valueSet, count, offset], (parse, session, token) =>
            TermSearch.SearchAsync(session, parse.GetValue(text), parse.GetValue(system), parse.GetValue(valueSet), parse.GetValue(count), parse.GetValue(offset), token));
    }

    private static Command Validate()
    {
        Argument<string> code = Required("code", "The code to check");
        var system = new Option<string?>("--system") { Description = "The code system url" };
        var display = new Option<string?>("--display") { Description = "A display to check" };
        var valueSet = new Option<string?>("--value-set") { Description = "A ValueSet url or url|version to check membership in" };
        Option<string?> version = Version();
        return Verb("validate", "Whether a code is in a CodeSystem or ValueSet, and whether a display matches", [code], [system, display, valueSet, version], (parse, session, token) =>
            TermValidate.ValidateAsync(session, parse.GetValue(code), parse.GetValue(system), parse.GetValue(display), parse.GetValue(valueSet), parse.GetValue(version), token));
    }

    private static Command Subsumes()
    {
        Argument<string> system = Required("system", "The code system url");
        Argument<string> codeA = Required("codeA", "Code A");
        Argument<string> codeB = Required("codeB", "Code B");
        Option<string?> version = Version();
        return Verb("subsumes", "Subsumption between two codes of one system", [system, codeA, codeB], [version], (parse, session, token) =>
            TermSubsumes.SubsumesAsync(session, parse.GetValue(system), parse.GetValue(codeA), parse.GetValue(codeB), parse.GetValue(version), token));
    }

    private static Command Tree()
    {
        Argument<string> system = Required("system", "The code system url");
        Argument<string> code = Required("code", "The code to start from");
        var direction = new Option<string?>("--direction") { Description = "descendants (default) or ancestors" };
        var depth = new Option<int?>("--depth") { Description = $"Levels to follow (default {TermNavigate.DefaultDepth}, maximum {TermNavigate.MaxDepth})" };
        (Option<int?> count, Option<int> offset) = Paging();
        Option<string?> version = Version();
        return Verb("tree", "Ancestors or descendants of a code, by depth", [system, code], [direction, depth, count, offset, version], (parse, session, token) =>
            TermNavigate.NavigateAsync(session, parse.GetValue(system), parse.GetValue(code), parse.GetValue(direction), parse.GetValue(depth), parse.GetValue(count), parse.GetValue(offset), parse.GetValue(version), token));
    }

    private static Command Expand()
    {
        Argument<string> valueSet = Required("valueSet", "The ValueSet url or url|version");
        var filter = new Option<string?>("--filter") { Description = "Keep codes whose display contains this text or whose code starts with it" };
        var scope = new Option<string?>("--scope") { Description = "all (default) or folder: only the codes the folder's resources carry, with counts and the searches that find them" };
        (Option<int?> count, Option<int> offset) = Paging();
        return Verb("expand", "A ValueSet's codes over the loaded terminology (isa/<code> for a SNOMED CT hierarchy)", [valueSet], [filter, scope, count, offset], (parse, session, token) =>
            parse.GetValue(scope) switch
            {
                TermExpandInFolder.FolderScope => Untyped(TermExpandInFolder.ExpandAsync(session, parse.GetValue(valueSet), parse.GetValue(filter), parse.GetValue(count), parse.GetValue(offset), token)),
                null or "all" => Untyped(TermExpand.ExpandAsync(session, parse.GetValue(valueSet), parse.GetValue(filter), parse.GetValue(count), parse.GetValue(offset), token)),
                string other => Task.FromResult(EnvelopeAssembler.Failure<object>(session, new IssueItem("error", "invalid-parameter", $"--scope is 'all' or 'folder', not '{QueryEcho.Value(other)}'.", Location: null), token)),
            });
    }

    private static Command Translate()
    {
        Argument<string> system = Required("system", "The source code system url");
        Argument<string> code = Required("code", "The source code");
        var targetSystem = new Option<string?>("--target-system") { Description = "Only targets in this code system" };
        var conceptMap = new Option<string?>("--concept-map") { Description = "Only this ConceptMap (url or url|version)" };
        return Verb("translate", "Source code to targets through the loaded ConceptMaps", [system, code], [targetSystem, conceptMap], (parse, session, token) =>
            TermTranslate.TranslateAsync(session, parse.GetValue(system), parse.GetValue(code), parse.GetValue(targetSystem), parse.GetValue(conceptMap), token));
    }

    private static Command Load()
    {
        Argument<string> package = Required("package", "id@version, or a bare id for the registry's latest version; downloaded from https://packages.fhir.org unless stored, cached or --offline");
        return Verb("load", "Load a FHIR npm package's terminology, downloading it from packages.fhir.org when it is not stored", [package], [], (parse, session, token) =>
            TermLoad.LoadAsync(session, parse.GetValue(package), token));
    }

    private static Command ClassSearch()
    {
        Argument<string> text = Required("text", "Words of the class name, for example \"beta blocker\" or statin; matched locally, never sent");
        Option<string?> source = Source("ATC, VA, EPC, MOA (default all), comma-separated");
        (Option<int?> count, Option<int> offset) = Paging();
        return Verb("class-search", "Drug classes by name, from NLM RxClass (needs --rxclass-server)", [text], [source, count, offset], (parse, session, token) =>
            TermClassSearch.SearchAsync(session, parse.GetValue(text), parse.GetValue(source), parse.GetValue(count), parse.GetValue(offset), token));
    }

    private static Command ClassMembers()
    {
        Argument<string> classId = Required("classId", "The RxClass class id, for example C07AB");
        Option<string?> source = Source("ATC, VA, EPC or MOA");
        source.Required = true;
        var scope = new Option<string?>("--scope") { Description = "folder (default: only codes the folder's medication resources carry) or all" };
        (Option<int?> count, Option<int> offset) = Paging();
        return Verb("class-members", "RxNorm codes of a drug class that the folder uses, from NLM RxClass (needs --rxclass-server)", [classId], [source, scope, count, offset], (parse, session, token) =>
            TermClassMembers.MembersAsync(session, parse.GetValue(classId), parse.GetValue(source), parse.GetValue(scope), parse.GetValue(count), parse.GetValue(offset), token));
    }

    private static Command Classes()
    {
        Argument<string> code = Required("code", "An RxNorm code (RxCUI), for example 866412");
        Option<string?> source = Source("ATC, VA, EPC, MOA (default ATC,VA), comma-separated");
        return Verb("classes", "The drug classes of an RxNorm code, from NLM RxClass (needs --rxclass-server)", [code], [source], (parse, session, token) =>
            TermClasses.ClassesAsync(session, parse.GetValue(code), parse.GetValue(source), token));
    }

    private static Option<string?> Source(string description) => new("--source") { Description = description };

    private static Command Verb<TFacts>(string name, string description, Argument[] arguments, Option[] options, Func<ParseResult, FolderSession, CancellationToken, Task<Contracts.ToolEnvelope<TFacts>>> run)
    {
        Argument<string> pathArgument = CommonOptions.CreatePathArgument();
        Option<string> versionOption = CommonOptions.CreateFhirVersionOption();
        Option<string?> cacheDirOption = CommonOptions.CreateCacheDirOption();
        Option<bool> noCacheOption = CommonOptions.CreateNoCacheOption();
        Option<bool> prettyOption = CommonOptions.CreatePrettyOption();
        var terminology = new TerminologyCliOptions();

        var command = new Command(name, description);
        command.Arguments.Add(pathArgument);
        foreach (Argument argument in arguments)
        {
            command.Arguments.Add(argument);
        }

        foreach (Option option in options)
        {
            command.Options.Add(option);
        }

        command.Options.Add(versionOption);
        command.Options.Add(prettyOption);
        CommonOptions.AddCacheOptions(command, cacheDirOption, noCacheOption);
        terminology.AddTo(command);
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            using ILoggerFactory loggerFactory = StandardErrorLogging.Create(LogLevel.Warning);
            FolderSession session = CommonOptions.CreateSession(parseResult, pathArgument, versionOption, cacheDirOption, noCacheOption, loggerFactory, terminology.Resolve(parseResult, cacheDirOption));
            return EnvelopeOutput.Write(await run(parseResult, session, cancellationToken), parseResult.GetValue(prettyOption));
        });

        return command;
    }

    /// <summary>A call's envelope with its facts typed as object, for a verb whose answer type depends on an option.</summary>
    private static async Task<ToolEnvelope<object?>> Untyped<TFacts>(Task<ToolEnvelope<TFacts?>> call)
        where TFacts : class
    {
        ToolEnvelope<TFacts?> envelope = await call;
        return new ToolEnvelope<object?>(envelope.Facts, envelope.Evidence, envelope.Omitted, envelope.Absent, envelope.Issues, envelope.Freshness) { Next = envelope.Next, AsOf = envelope.AsOf };
    }

    private static Argument<string> Required(string name, string description) => new(name) { Description = description };

    private static Option<string?> Version() => new("--version") { Description = "The code system version; omit for the loaded default" };

    private static (Option<int?> Count, Option<int> Offset) Paging() =>
        (new Option<int?>("--count") { Description = "Items per page (default 10, maximum 50)" }, new Option<int>("--offset") { Description = "Items to skip" });
}
