// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System.Text.Json;
using System.Text.Json.Serialization;
using Ignixa.Search.Indexing.SearchValues;

namespace Ignixa.Search.Serialization;

/// <summary>
/// System.Text.Json converter for one <see cref="ISearchValue"/> in the lossless compact format, for stores that persist
/// values individually rather than as a <see cref="CompactSearchIndexConverter"/> index.
/// </summary>
/// <remarks>Reading anything the format did not write throws <see cref="JsonException"/>.</remarks>
public sealed class CompactSearchValueConverter : JsonConverter<ISearchValue>
{
    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, ISearchValue value, JsonSerializerOptions options) =>
        CompactSearchValueWriter.WriteObject(writer, value);

    /// <inheritdoc />
    public override ISearchValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        CompactSearchValueReader.Read(ref reader);
}
