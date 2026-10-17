// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.CommandLine;
using Ignixa.CliOnFhir.Reading;
using Ignixa.CliOnFhir.Session;
using Microsoft.Extensions.Logging;

namespace Ignixa.CliOnFhir.Commands;

/// <summary><c>cli-on-fhir get &lt;path&gt; &lt;ref&gt;...</c>: the CLI form of the <c>read_resources</c> MCP tool.</summary>
internal static class GetCommand
{
    public static Command Create()
    {
        Argument<string> pathArgument = CommonOptions.CreatePathArgument();
        var referencesArgument = new Argument<string[]>("ref")
        {
            Description = $"One or more resource references, for example Patient/123 (at most {ResourceFetcher.MaxReferences})",
            Arity = ArgumentArity.OneOrMore,
        };

        Option<string> versionOption = CommonOptions.CreateFhirVersionOption();
        var elementsOption = new Option<string?>("--elements")
        {
            Description = "Comma-separated top-level elements to keep (resourceType, id and meta are always kept), for example code,valueQuantity",
        };

        Option<int?> maxBytesOption = CommonOptions.CreateMaxBytesOption();
        Option<string?> cacheDirOption = CommonOptions.CreateCacheDirOption();
        Option<bool> noCacheOption = CommonOptions.CreateNoCacheOption();
        Option<bool> prettyOption = CommonOptions.CreatePrettyOption();

        var command = new Command("get", "Read resources by reference with the file location of each. Exits non-zero, with the problem in 'issues', when a reference is malformed");
        command.Arguments.Add(pathArgument);
        command.Arguments.Add(referencesArgument);
        command.Options.Add(versionOption);
        command.Options.Add(elementsOption);
        command.Options.Add(maxBytesOption);
        command.Options.Add(prettyOption);
        CommonOptions.AddCacheOptions(command, cacheDirOption, noCacheOption);

        command.SetAction((parseResult, cancellationToken) =>
        {
            using ILoggerFactory loggerFactory = StandardErrorLogging.Create(LogLevel.Warning);
            FolderSession session = CommonOptions.CreateSession(parseResult, pathArgument, versionOption, cacheDirOption, noCacheOption, loggerFactory);
            return Task.FromResult(EnvelopeOutput.Write(ResourceFetcher.Read(session, parseResult.GetValue(referencesArgument)!, parseResult.GetValue(elementsOption), parseResult.GetValue(maxBytesOption), cancellationToken), parseResult.GetValue(prettyOption)));
        });

        return command;
    }
}
