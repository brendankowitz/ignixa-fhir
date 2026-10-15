// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Ignixa.Abstractions;
using Ignixa.CliOnFhir.Catalog;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Reading;
using Ignixa.Search.Generated;

namespace Ignixa.CliOnFhir.CodeSystems;

/// <summary>Builds (once per catalog) the coding inventory of every catalogued resource, reading each file once.</summary>
public static class CodeSystemInventories
{
    private const int ProblemSamples = 5;

    private static readonly ConditionalWeakTable<FolderCatalog, Lazy<CodeSystemInventory>> Cache = [];

    public static CodeSystemInventory For(FolderCatalog catalog, FhirVersion version)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        return Cache.GetValue(catalog, key => new Lazy<CodeSystemInventory>(() => Build(key, version), LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    /// <summary>The FHIR <c>code</c> elements with a FHIR-defined binding, by path, from Ignixa's generated element map for <paramref name="version"/>.</summary>
    public static IReadOnlySet<string> ImplicitElements(FhirVersion version) => version switch
    {
        FhirVersion.Stu3 => STU3CodeSystemMappings.GetMappings().Keys.ToHashSet(StringComparer.Ordinal),
        FhirVersion.R4 => R4CodeSystemMappings.GetMappings().Keys.ToHashSet(StringComparer.Ordinal),
        FhirVersion.R4B => R4BCodeSystemMappings.GetMappings().Keys.ToHashSet(StringComparer.Ordinal),
        FhirVersion.R5 => R5CodeSystemMappings.GetMappings().Keys.ToHashSet(StringComparer.Ordinal),
        _ => R6CodeSystemMappings.GetMappings().Keys.ToHashSet(StringComparer.Ordinal),
    };

    private static CodeSystemInventory Build(FolderCatalog catalog, FhirVersion version)
    {
        var stopwatch = Stopwatch.StartNew();
        IReadOnlySet<string> implicitElements = ImplicitElements(version);
        var builder = new InventoryBuilder();
        List<IssueItem> problems = [];
        int read = 0;
        int unreadable = 0;
        foreach (IGrouping<string, CatalogEntry> file in catalog.Entries.GroupBy(entry => entry.FilePath, StringComparer.Ordinal))
        {
            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(Path.Combine(catalog.RootPath, file.Key));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                unreadable += file.Count();
                Add(problems, ResourceReader.Unreadable(file.First(), ex));
                continue;
            }

            foreach (CatalogEntry entry in file)
            {
                (ReadOnlyMemory<byte>? json, IssueItem? problem) = ResourceReader.Slice(bytes, entry);
                if (json is not { } resource)
                {
                    unreadable++;
                    Add(problems, problem!);
                    continue;
                }

                try
                {
                    using JsonDocument document = JsonDocument.Parse(resource);
                    CodingWalker.Walk(document.RootElement, entry.ResourceType, FhirIds.Reference(entry), implicitElements, builder);
                    read++;
                }
                catch (JsonException ex)
                {
                    unreadable++;
                    Add(problems, new IssueItem("warning", "invalid-resource", $"{FhirIds.Reference(entry)} could not be parsed for the coding inventory ({ex.GetType().Name}); its codings are not counted.", entry.Line is int line ? $"{entry.FilePath}:{line}" : entry.FilePath));
                }
            }
        }

        if (unreadable > ProblemSamples)
        {
            problems.Add(new IssueItem("warning", "resource-unreadable", $"{unreadable} resources in all could not be read for the coding inventory; their codings are not counted.", Location: null));
        }

        (IReadOnlyList<SystemTally> systems, IReadOnlyList<ImplicitTally> implicitCodes, IReadOnlyList<ElementCount> withoutSystem) = builder.Build();
        return new CodeSystemInventory(systems, implicitCodes, withoutSystem, read, problems, stopwatch.ElapsedMilliseconds);
    }

    private static void Add(List<IssueItem> problems, IssueItem problem)
    {
        if (problems.Count < ProblemSamples)
        {
            problems.Add(problem with { Severity = "warning" });
        }
    }
}
