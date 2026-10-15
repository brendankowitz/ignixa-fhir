// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics;
using Ignixa.Abstractions;
using Ignixa.CliOnFhir.Catalog;
using Ignixa.CliOnFhir.Indexing;
using Microsoft.Extensions.Logging;

namespace Ignixa.CliOnFhir.Session;

/// <summary>
/// One folder opened by the tool: builds the catalog and index on first use and shares them
/// between every CLI verb and MCP tool call in the process.
/// </summary>
public sealed class FolderSession
{
    private readonly Lazy<(FolderIndex Index, BuildStats Stats)> _build;

    public FolderSession(string rootPath, IFhirSchemaProvider schema, ILoggerFactory loggerFactory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        RootPath = Path.GetFullPath(rootPath);
        Schema = schema;
        _build = new Lazy<(FolderIndex, BuildStats)>(
            () => Build(RootPath, schema, loggerFactory),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public string RootPath { get; }

    public IFhirSchemaProvider Schema { get; }

    public FolderIndex Index => _build.Value.Index;

    public BuildStats Stats => _build.Value.Stats;

    private static (FolderIndex, BuildStats) Build(string rootPath, IFhirSchemaProvider schema, ILoggerFactory loggerFactory)
    {
        var stopwatch = Stopwatch.StartNew();
        FolderCatalog catalog = FolderCatalogBuilder.Build(rootPath, CancellationToken.None);
        long catalogMilliseconds = stopwatch.ElapsedMilliseconds;

        stopwatch.Restart();
        FolderIndex index = new FolderIndexBuilder(schema, loggerFactory).Build(catalog, CancellationToken.None);
        long indexMilliseconds = stopwatch.ElapsedMilliseconds;

        long heap = GC.GetTotalMemory(forceFullCollection: true);
        long workingSet = Environment.WorkingSet;
        return (index, new BuildStats(catalogMilliseconds, indexMilliseconds, heap, workingSet));
    }
}
