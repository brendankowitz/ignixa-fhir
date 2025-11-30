// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Serialization;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Serialization.Models;

/// <summary>
/// JSON converter factory that handles all BaseJsonNode-derived types.
/// Automatically creates the appropriate BaseJsonNodeConverter&lt;T&gt; for any derived type.
/// </summary>
public class JsonNodeConverterFactory : JsonConverterFactory
{
    /// <summary>
    /// Determines whether this factory can convert the specified type.
    /// Returns true for BaseJsonNode and all derived types.
    /// </summary>
    public override bool CanConvert(Type typeToConvert)
    {
        return typeof(BaseJsonNode).IsAssignableFrom(typeToConvert);
    }

    /// <summary>
    /// Creates a BaseJsonNodeConverter&lt;T&gt; instance for the specific derived type.
    /// </summary>
    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var converterType = typeof(JsonNodeConverter<>).MakeGenericType(typeToConvert);
        return (JsonConverter)Activator.CreateInstance(converterType)!;
    }
}
