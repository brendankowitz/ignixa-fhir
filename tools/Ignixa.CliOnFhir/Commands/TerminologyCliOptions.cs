// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.CommandLine;
using Ignixa.CliOnFhir.Caching;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Terminology;
using Ignixa.CliOnFhir.Terminology.Npm;
using Ignixa.CliOnFhir.Terminology.Remote;

namespace Ignixa.CliOnFhir.Commands;

/// <summary>The options that say where terminology comes from, shared by <c>mcp</c>, <c>inspect</c> and the <c>term</c> verbs.</summary>
internal sealed class TerminologyCliOptions
{
    public Option<string[]> Packages { get; } = new("--terminology")
    {
        Description = "A FHIR npm package to load on the first terminology call, id@version (repeatable), for example hl7.terminology.r4@7.4.0. "
            + "Without any, the FHIR version's core package (hl7.fhir.r4.core@4.0.1 for R4) is loaded",
        AllowMultipleArgumentsPerToken = false,
    };

    public Option<bool> Offline { get; } = new("--offline")
    {
        Description = "Never use the network: packages load only from the package store or the FHIR package cache",
    };

    public Option<string?> FhirPackageCache { get; } = new("--fhir-package-cache")
    {
        Description = "The standard FHIR package cache to read packages from, read only (default: ~/.fhir/packages when it exists)",
    };

    public Option<string?> TxServer { get; } = new("--tx-server")
    {
        Description = "A FHIR terminology server (for example https://tx.fhir.org/r4) asked only for code systems, ValueSets and ConceptMaps "
            + "not loaded locally (SNOMED CT, LOINC, ...). Off by default; requests carry only codes, systems, versions, displays, urls and filter text, "
            + "never resource content. Not with --offline",
    };

    public Option<string?> RxClassServer { get; } = new("--rxclass-server")
    {
        Description = "NLM RxNav (https://rxnav.nlm.nih.gov/REST) for drug classes (ATC, VA, EPC, MoA) through RxClass. Off by default; requests carry "
            + "only RxCUIs, class ids and class types, never resource content or search text. Answers are cached under the cache root. Not with --offline",
    };

    public void AddTo(Command command)
    {
        ArgumentNullException.ThrowIfNull(command);

        command.Options.Add(Packages);
        command.Options.Add(Offline);
        command.Options.Add(FhirPackageCache);
        command.Options.Add(TxServer);
        command.Options.Add(RxClassServer);
        command.Validators.Add(result =>
        {
            if (result.GetValue(RxClassServer) is { } rxClass)
            {
                if (result.GetValue(Offline))
                {
                    result.AddError("--rxclass-server and --offline cannot be used together: --offline disables the network.");
                }
                else if (!Uri.TryCreate(rxClass, UriKind.Absolute, out Uri? uri))
                {
                    result.AddError($"--rxclass-server {rxClass}: not an absolute URL.");
                }
                else if (RxClassClient.Validate(uri) is { } problem)
                {
                    result.AddError($"--rxclass-server {rxClass}: {problem}");
                }
            }

            if (result.GetValue(TxServer) is { } server)
            {
                if (result.GetValue(Offline))
                {
                    result.AddError("--tx-server and --offline cannot be used together: --offline disables the network.");
                }
                else if (!Uri.TryCreate(server, UriKind.Absolute, out Uri? uri))
                {
                    result.AddError($"--tx-server {server}: not an absolute URL.");
                }
                else if (TxServerClient.Validate(uri) is { } problem)
                {
                    result.AddError($"--tx-server {server}: {problem}");
                }
            }

            foreach (string package in result.GetValue(Packages) ?? [])
            {
                try
                {
                    _ = PackageRequest.Parse(package);
                }
                catch (ToolRequestException ex)
                {
                    result.AddError($"--terminology {package}: {ex.Issue.Diagnostics}");
                }
            }
        });
    }

    /// <summary>The session's terminology options. The package store and the RxClass answers sit under the index cache root (<c>--cache-dir</c> or the user cache directory).</summary>
    public TerminologyOptions Resolve(ParseResult parseResult, Option<string?> cacheDirOption)
    {
        ArgumentNullException.ThrowIfNull(parseResult);

        IndexCacheOptions root = parseResult.GetValue(cacheDirOption) is { } cacheDir ? IndexCacheOptions.At(cacheDir) : IndexCacheOptions.UserDefault();
        string? fhirCache = parseResult.GetValue(FhirPackageCache) ?? DefaultFhirPackageCache();
        return new TerminologyOptions
        {
            Packages = [.. (parseResult.GetValue(Packages) ?? []).Select(PackageRequest.Parse)],
            Store = root.RootDirectory is { } directory ? PackageStore.Under(directory) : null,
            FhirPackageCache = fhirCache,
            Registry = parseResult.GetValue(Offline) ? null : new FhirPackageRegistry(),
            TxServer = parseResult.GetValue(TxServer) is { } server ? new TxServerClient(new Uri(server)) : null,
            RxClass = parseResult.GetValue(RxClassServer) is { } rxClass ? new RxClassClient(new Uri(rxClass), root.RootDirectory is { } cacheRoot ? Path.Combine(cacheRoot, "rxclass") : null) : null,
        };
    }

    private static string? DefaultFhirPackageCache()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string cache = Path.Combine(home, ".fhir", "packages");
        return home.Length > 0 && Directory.Exists(cache) ? cache : null;
    }
}
