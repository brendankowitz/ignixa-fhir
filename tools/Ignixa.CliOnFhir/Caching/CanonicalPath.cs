// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Caching;

/// <summary>
/// Turns a path into the one a file system call would end up at, by following links in every component that
/// exists. Two different-looking paths (a symlinked cache directory, <c>/tmp</c> versus <c>/private/tmp</c>) can
/// name the same directory; comparing them needs the resolved form.
/// </summary>
internal static class CanonicalPath
{
    private const int MaxLinkHops = 40;

    /// <summary>
    /// Resolves <paramref name="path"/> against the file system the way the operating system does: one component at
    /// a time, left to right, expanding each link where it stands. A relative link target is read relative to the
    /// link's own directory, and a <c>..</c> means the parent of what the components before it resolved to, which
    /// is not the lexical parent when one of them was a link. Components that do not exist yet (a cache directory
    /// not created so far) are kept as written.
    /// </summary>
    /// <exception cref="IOException">A link chain is too long or cyclic.</exception>
    public static string Resolve(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string absolute = Path.IsPathRooted(path) ? path : Path.Combine(Directory.GetCurrentDirectory(), path);
        string current = Path.GetPathRoot(absolute)!;
        Stack<string> pending = new();
        PushSegments(pending, absolute[current.Length..]);
        int hops = 0;
        while (pending.TryPop(out string? segment))
        {
            if (segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                current = Path.GetDirectoryName(current) ?? current;
                continue;
            }

            string next = Path.Combine(current, segment);
            string? linkTarget = new DirectoryInfo(next).LinkTarget;
            if (linkTarget is null)
            {
                current = next;
                continue;
            }

            if (++hops > MaxLinkHops)
            {
                throw new IOException($"Too many levels of links while resolving '{path}'.");
            }

            // An absolute target restarts from its root; a relative one continues from the link's directory, which
            // is where `current` already is. Either way its components are resolved before anything after the link.
            if (Path.IsPathRooted(linkTarget))
            {
                current = Path.GetPathRoot(linkTarget)!;
                PushSegments(pending, linkTarget[current.Length..]);
            }
            else
            {
                PushSegments(pending, linkTarget);
            }
        }

        return current;
    }

    // Pushes the segments so that the first one ends up on top.
    private static void PushSegments(Stack<string> stack, string path)
    {
        string[] segments = path.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        for (int i = segments.Length - 1; i >= 0; i--)
        {
            stack.Push(segments[i]);
        }
    }
}
