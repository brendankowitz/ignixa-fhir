// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;

namespace Ignixa.Hl7v2.LogicalModel;

public static class Hl7v2LogicalPath
{
    public static IElement? SelectSingle(IElement root, string path)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var segments = path.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return null;
        }

        var current = root;
        var startIndex = IsRootSegment(root, segments[0]) ? 1 : 0;

        for (var i = startIndex; i < segments.Length; i++)
        {
            var (name, index) = ParseSegment(segments[i]);
            var children = current.Children(name);
            if (children.Count == 0 || index >= children.Count)
            {
                return null;
            }

            current = children[index];
        }

        return current;
    }

    private static bool IsRootSegment(IElement root, string segment)
    {
        return string.Equals(segment, root.Name, StringComparison.Ordinal)
            || string.Equals(segment, root.InstanceType, StringComparison.Ordinal);
    }

    private static (string Name, int Index) ParseSegment(string segment)
    {
        var bracketIndex = segment.IndexOf('[', StringComparison.Ordinal);
        if (bracketIndex < 0)
        {
            return (segment, 0);
        }

        var closeBracketIndex = segment.IndexOf(']', bracketIndex);
        if (closeBracketIndex < 0)
        {
            throw new ArgumentException($"Invalid indexed path segment '{segment}'", nameof(segment));
        }

        var name = segment[..bracketIndex];
        var indexText = segment[(bracketIndex + 1)..closeBracketIndex];
        return (name, int.Parse(indexText, System.Globalization.CultureInfo.InvariantCulture));
    }
}
