// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ignixa.SqlOnFhir.Fabric;

/// <summary>
/// Rejects a source resource holding a string or property name that is not valid Unicode, anywhere in the resource
/// (the policy <see cref="StrictJson"/> applies to contracts). Parsed text can carry a lone surrogate escape, which
/// fails when decoded; a resource built in code can hold a lone surrogate, which every JSON or UTF-8 writer silently
/// replaces with U+FFFD, so ids, cells and companions would otherwise be stored corrupted.
/// </summary>
/// <remarks>
/// Property names are resource data, which may be sensitive or hold line breaks, so a rejection path shows a name only
/// when the contract names it (or it is <c>_x</c> for such a name <c>x</c>); any other appears as
/// <c>&lt;property N&gt;</c>, its zero-based position in its object. Instances are immutable and thread-safe.
/// </remarks>
internal sealed class SilverSourceText
{
    public const string InvalidReason = "contains text that is not valid Unicode";

    private const string ShadowPrefix = "_";

    // Read from every resource, whatever columns the contract has besides the id it must have.
    private static readonly string[] AlwaysRead = ["resourceType", "meta", "lastUpdated"];

    private readonly FrozenSet<string> _pathNames;

    /// <param name="columns">The contract's columns, whose field names at any depth may appear in rejection paths.</param>
    public SilverSourceText(IEnumerable<SilverField> columns)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in AlwaysRead)
        {
            AddPathName(name, names);
        }

        AddFieldNames(columns, names);
        _pathNames = names.ToFrozenSet(StringComparer.Ordinal);
    }

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

    /// <param name="root">The resource.</param>
    /// <param name="rootPath">The path reported for a property name of the resource itself.</param>
    /// <exception cref="FabricSilverProjectionException">
    /// At the path of the invalid string, or of the object whose property name is invalid.
    /// </exception>
    public void Validate(JsonObject root, string rootPath) => ValidateMembers(root, null, rootPath);

    private static void AddFieldNames(IEnumerable<SilverField> fields, HashSet<string> names)
    {
        foreach (var field in fields)
        {
            AddPathName(field.Name, names);
            AddPathName(field.SourceName, names);
            AddTypeNames(field.Type, names);
        }
    }

    private static void AddTypeNames(SilverType type, HashSet<string> names)
    {
        AddFieldNames(type.Fields, names);
        if (type.Element is { } element)
        {
            AddTypeNames(element, names);
        }
    }

    private static void AddPathName(string name, HashSet<string> names)
    {
        names.Add(name);
        names.Add(ShadowPrefix + name);
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

    private void Validate(JsonNode? node, string path)
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
    private void ValidateMembers(JsonObject members, string? path, string reportedPath)
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

        var position = 0;
        foreach (var (name, member) in members)
        {
            if (!IsWellFormed(name))
            {
                throw Reject(reportedPath);
            }

            var shown = _pathNames.Contains(name) ? name : $"<property {position}>";
            Validate(member, path is null ? shown : SilverValueReader.Child(path, shown));
            position++;
        }
    }
}