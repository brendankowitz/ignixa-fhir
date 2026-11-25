// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ignixa.SqlOnFhir.Fabric;

/// <summary>
/// Rejects a source resource holding a string or property name that is not valid Unicode, anywhere in the resource
/// (the policy <see cref="StrictJson"/> applies to contracts). Parsed text can carry a lone surrogate escape, which
/// fails when decoded; a resource built in code can hold a lone surrogate, which every JSON or UTF-8 writer silently
/// replaces with U+FFFD, so ids, cells and companions would otherwise be stored corrupted.
/// </summary>
internal static class SilverSourceText
{
    public const string InvalidReason = "contains text that is not valid Unicode";

    /// <param name="root">The resource.</param>
    /// <param name="rootPath">The path reported for a property name of the resource itself.</param>
    /// <exception cref="FabricSilverProjectionException">
    /// At the path of the invalid string, or of the object whose property name is invalid.
    /// </exception>
    public static void Validate(JsonObject root, string rootPath) => ValidateMembers(root, null, rootPath);

    /// <summary>Whether the text is well-formed UTF-16: every surrogate is part of a high-low pair.</summary>
    public static bool IsWellFormed(string text)
    {
        var rest = text.AsSpan();
        int index;
        while ((index = rest.IndexOfAnyInRange('\uD800', '\uDFFF')) >= 0)
        {
            if (!char.IsHighSurrogate(rest[index]) || index + 1 == rest.Length || !char.IsLowSurrogate(rest[index + 1]))
            {
                return false;
            }

            rest = rest[(index + 2)..];
        }

        return true;
    }

    private static void Validate(JsonNode? node, string path)
    {
        switch (node)
        {
            case null:
                break;
            case JsonObject members:
                ValidateMembers(members, path, path);
                break;
            case JsonArray items:
                for (var index = 0; index < items.Count; index++)
                {
                    Validate(items[index], $"{path}[{index}]");
                }

                break;
            default:
                var value = node.AsValue();
                if (value.GetValueKind() == JsonValueKind.String && !IsWellFormed(Decode(value, path)))
                {
                    throw Reject(path);
                }

                break;
        }
    }

    /// <param name="members">The object.</param>
    /// <param name="path">The object's path, or null for the resource, whose members' paths are their bare names.</param>
    /// <param name="reportedPath">The path reported when a property name is invalid.</param>
    private static void ValidateMembers(JsonObject members, string? path, string reportedPath)
    {
        try
        {
            // The first access to a parsed object decodes all of its property names.
            _ = members.Count;
        }
        catch (InvalidOperationException)
        {
            // The message is not chained because it is about input text.
            throw Reject(reportedPath);
        }

        foreach (var (name, member) in members)
        {
            if (!IsWellFormed(name))
            {
                throw Reject(reportedPath);
            }

            Validate(member, path is null ? name : SilverValueReader.Child(path, name));
        }
    }

    private static string Decode(JsonValue value, string path)
    {
        try
        {
            return value.GetValue<string>();
        }
        catch (InvalidOperationException)
        {
            // Raised for a lone surrogate escape; the message is not chained because it is about input text.
            throw Reject(path);
        }
    }

    private static FabricSilverProjectionException Reject(string path) => SilverValueReader.Reject(path, InvalidReason);
}
