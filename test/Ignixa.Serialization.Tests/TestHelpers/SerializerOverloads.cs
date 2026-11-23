// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;
using System.Text.Json;
using Ignixa.Serialization.SourceNodes;
using Xunit;

namespace Ignixa.Serialization.Tests.TestHelpers;

/// <summary>
/// Runs each public <c>JsonSourceNodeFactory</c> serialization overload with its defaults, so a test can
/// assert that every path writes the same output.
/// </summary>
internal static class SerializerOverloads
{
    public static TheoryData<string> All =>
    [
        "string", "bytes", "stream", "streamAsync",
        "string-options", "bytes-options", "stream-options", "streamAsync-options",
    ];

    public static async Task<string> SerializeAsync(ResourceJsonNode resource, string overload)
    {
        var options = new JsonWriterOptions();
        switch (overload)
        {
            case "string":
                return resource.SerializeToString();
            case "string-options":
                return resource.SerializeToString(options);
            case "bytes":
                return Encoding.UTF8.GetString(resource.SerializeToBytes().Span);
            case "bytes-options":
                return Encoding.UTF8.GetString(resource.SerializeToBytes(options).Span);
            case "stream":
                return SerializeToStream(stream => resource.SerializeToStream(stream));
            case "stream-options":
                return SerializeToStream(stream => resource.SerializeToStream(stream, options));
            case "streamAsync":
            {
                using var stream = new MemoryStream();
                await resource.SerializeToStreamAsync(stream);
                return Encoding.UTF8.GetString(stream.ToArray());
            }

            case "streamAsync-options":
            {
                using var stream = new MemoryStream();
                await resource.SerializeToStreamAsync(stream, options);
                return Encoding.UTF8.GetString(stream.ToArray());
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(overload), overload, null);
        }
    }

    private static string SerializeToStream(Action<MemoryStream> serialize)
    {
        using var stream = new MemoryStream();
        serialize(stream);
        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
