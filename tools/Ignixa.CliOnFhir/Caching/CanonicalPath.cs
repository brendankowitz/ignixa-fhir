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
    /// Resolves <paramref name="path"/> against the file system. Components that do not exist yet (a cache directory
    /// not created so far) are kept as written, appended to the resolved form of their deepest existing ancestor.
    /// </summary>
    /// <exception cref="IOException">A link chain is too long or cyclic, or a link cannot be read.</exception>
    public static string Resolve(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string full = Path.GetFullPath(path);
        string current = Path.GetPathRoot(full)!;
        Stack<string> pending = new();
        PushSegments(pending, full, current);
        int hops = 0;
        while (pending.TryPop(out string? segment))
        {
            string next = Path.Combine(current, segment);
            if (new DirectoryInfo(next).LinkTarget is null)
            {
                current = next;
                continue;
            }

            if (++hops > MaxLinkHops)
            {
                throw new IOException($"Too many levels of links while resolving '{path}'.");
            }

            // The target's own ancestors may be links too, so it is walked component by component as well.
            string target = new DirectoryInfo(next).ResolveLinkTarget(returnFinalTarget: true)?.FullName
                ?? throw new IOException($"The link '{next}' has no resolvable target.");
            current = Path.GetPathRoot(target)!;
            PushSegments(pending, target, current);
        }

        return current;
    }

    // Pushes the segments below the root so that the first one ends up on top.
    private static void PushSegments(Stack<string> stack, string fullPath, string root)
    {
        string[] segments = fullPath[root.Length..].Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        for (int i = segments.Length - 1; i >= 0; i--)
        {
            stack.Push(segments[i]);
        }
    }
}
