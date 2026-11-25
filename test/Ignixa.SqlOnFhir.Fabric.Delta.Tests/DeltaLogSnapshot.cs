// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;

namespace Ignixa.SqlOnFhir.Fabric.Delta.Tests;

/// <summary>
/// The state of a Delta table at its latest JSON commit, replayed by <see cref="DeltaLogReader"/> without any Delta
/// engine. Elements are detached copies, valid after the log files change.
/// </summary>
internal sealed record DeltaLogSnapshot(
    long Version,
    JsonElement Protocol,
    JsonElement MetaData,
    IReadOnlyList<KeyValuePair<string, string>> Configuration,
    IReadOnlyDictionary<string, JsonElement> LiveFiles,
    IReadOnlyList<JsonElement> RemovedFiles,
    IReadOnlyList<DeltaLogChangeFile> ChangeFiles)
{
    /// <summary>The <c>metaData.schemaString</c>.</summary>
    public string SchemaString => MetaData.GetProperty("schemaString").GetString()!;

    /// <summary>The <c>metaData</c> property names in logged order.</summary>
    public IReadOnlyList<string> MetaDataKeys => [.. MetaData.EnumerateObject().Select(property => property.Name)];
}
