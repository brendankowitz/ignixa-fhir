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

namespace Ignixa.CliOnFhir.CodeSystems;

/// <summary>
/// Builds (once per catalog) the coding inventory of every catalogued resource, reading each file once. A cancelled build
/// is not kept, so the next request builds it afresh.
/// </summary>
public static class CodeSystemInventories
{
    private const int ProblemSamples = 5;

    private static readonly ConditionalWeakTable<FolderCatalog, Lazy<CodeSystemInventory>> Cache = [];

    public static CodeSystemInventory For(FolderCatalog catalog, IFhirSchemaProvider schema, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(schema);

        while (true)
        {
            Lazy<CodeSystemInventory> inventory = Cache.GetValue(catalog, key => new Lazy<CodeSystemInventory>(() => Build(key, schema, cancellationToken), LazyThreadSafetyMode.ExecutionAndPublication));
            try
            {
                return inventory.Value;
            }
            catch (OperationCanceledException ex) when (ex.CancellationToken.IsCancellationRequested)
            {
                // The Lazy keeps the cancellation; drop it. When it was another caller's, this caller builds it itself. A
                // failure, or an OperationCanceledException no token asked for, is not retried.
                Forget(catalog, inventory);
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
    }

    // Entries are only ever added when absent, so under the lock the entry cannot change between the check and the removal.
    private static void Forget(FolderCatalog catalog, Lazy<CodeSystemInventory> cancelled)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(catalog, out Lazy<CodeSystemInventory>? current) && ReferenceEquals(current, cancelled))
            {
                Cache.Remove(catalog);
            }
        }
    }

    private static CodeSystemInventory Build(FolderCatalog catalog, IFhirSchemaProvider schema, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        ImplicitCodeElements implicitElements = ImplicitCodeElements.From(schema);
        var builder = new InventoryBuilder();
        List<IssueItem> problems = [];
        int read = 0;
        int unreadable = 0;
        foreach (IGrouping<string, CatalogEntry> file in catalog.Entries.GroupBy(entry => entry.FilePath, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
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
                cancellationToken.ThrowIfCancellationRequested();
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
