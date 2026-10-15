// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.CommandLine;
using Ignixa.CliOnFhir.Caching;
using Ignixa.CliOnFhir.Session;
using Microsoft.Extensions.Logging;

namespace Ignixa.CliOnFhir.Commands;

/// <summary>Arguments and options shared by every verb.</summary>
internal static class CommonOptions
{
    public static Argument<string> CreatePathArgument() => new("path") { Description = "Folder of FHIR resources (JSON, NDJSON or Bundles)" };

    public static Option<string> CreateFhirVersionOption()
    {
        var option = new Option<string>("--fhir-version")
        {
            Description = "FHIR version of the folder's resources",
            DefaultValueFactory = _ => FhirSchemas.DefaultVersion,
        };
        option.AcceptOnlyFromAmong([.. FhirSchemas.SupportedVersions]);
        return option;
    }

    public static Option<bool> CreatePrettyOption() => new("--pretty")
    {
        Description = "Indent the JSON output (default: compact, the size an MCP client would receive)",
    };

    public static Option<string?> CreateCacheDirOption() => new("--cache-dir")
    {
        Description = "Root directory for the persisted index cache (default: the user cache directory, "
            + "$XDG_CACHE_HOME/cli-on-fhir, ~/.cache/cli-on-fhir or %LOCALAPPDATA%\\cli-on-fhir). The cache is never written inside the data folder",
    };

    public static Option<bool> CreateNoCacheOption() => new("--no-cache")
    {
        Description = "Neither read nor write the persisted index cache; build every index in memory",
    };

    /// <summary>Rejects <c>--cache-dir</c> together with <c>--no-cache</c>, which contradict each other.</summary>
    public static void AddCacheOptions(Command command, Option<string?> cacheDirOption, Option<bool> noCacheOption)
    {
        command.Options.Add(cacheDirOption);
        command.Options.Add(noCacheOption);
        command.Validators.Add(result =>
        {
            if (result.GetValue(cacheDirOption) is not null && result.GetValue(noCacheOption))
            {
                result.AddError("--cache-dir and --no-cache cannot be used together.");
            }
        });
    }

    public static IndexCacheOptions ResolveCache(ParseResult parseResult, Option<string?> cacheDirOption, Option<bool> noCacheOption)
    {
        ArgumentNullException.ThrowIfNull(parseResult);

        if (parseResult.GetValue(noCacheOption))
        {
            return IndexCacheOptions.Disabled;
        }

        return parseResult.GetValue(cacheDirOption) is { } cacheDir ? IndexCacheOptions.At(cacheDir) : IndexCacheOptions.UserDefault();
    }

    public static FolderSession CreateSession(ParseResult parseResult, Argument<string> pathArgument, Option<string> versionOption, Option<string?> cacheDirOption, Option<bool> noCacheOption, ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(parseResult);

        return new FolderSession(
            parseResult.GetValue(pathArgument)!,
            FhirSchemas.Resolve(parseResult.GetValue(versionOption)!),
            loggerFactory,
            ResolveCache(parseResult, cacheDirOption, noCacheOption));
    }
}
