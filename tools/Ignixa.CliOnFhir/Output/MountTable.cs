// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;

namespace Ignixa.CliOnFhir.Output;

/// <summary>
/// Where a path really is on its file system, read from Linux's <c>/proc/self/mountinfo</c>: the device of the mount it falls under
/// and the path within that file system (the mount's root, then the rest of the path). A bind mount of a part of the data folder is
/// a directory under another path with its own device and inode, so neither a path comparison nor the folder's identity sees it;
/// under this mapping it is a path under the folder's own path on the same device. An overlay mount writes into its upper directory,
/// which its options name (<see cref="OverlayWriteDirectories"/>). Elsewhere it is not <see cref="IsAvailable"/>.
/// </summary>
internal static class MountTable
{
    private const string MountInfo = "/proc/self/mountinfo";

    /// <summary>Whether mounts can be read on this system.</summary>
    public static bool IsAvailable { get; } = OperatingSystem.IsLinux() && File.Exists(MountInfo);

    /// <summary>The device (<c>major:minor</c>) and the path within its file system of <paramref name="path"/>, an absolute, resolved path.</summary>
    /// <exception cref="IOException">The mount table cannot be read.</exception>
    public static (string Device, string Path) Underlying(string path)
    {
        (string device, string root, string mountPoint, _, _) = Holding(path);
        string rest = mountPoint == "/" ? path : path[mountPoint.Length..];
        string underlying = root == "/" ? (rest.Length == 0 ? "/" : rest) : root + rest;
        return (device, underlying);
    }

    /// <summary>
    /// The upper and work directories (<c>upperdir=</c>, <c>workdir=</c>) of the overlay mount that holds <paramref name="path"/>, an
    /// absolute, resolved path, as its options name them; empty when that mount is not an overlay. A file written under an overlay is
    /// written into its upper directory, and the work directory holds the overlay's own temporary files.
    /// </summary>
    /// <exception cref="IOException">The mount table cannot be read.</exception>
    public static IReadOnlyList<string> OverlayWriteDirectories(string path)
    {
        (_, _, _, string fileSystem, string superOptions) = Holding(path);
        if (fileSystem is not ("overlay" or "overlayfs"))
        {
            return [];
        }

        // mountinfo escapes a comma inside a path as \054, so the options split on commas before they are unescaped; what is left is
        // the option as given at mount time, in which overlayfs takes a backslash to quote the next character (u\,p is u,p).
        return [.. superOptions.Split(',')
            .Where(option => option.StartsWith("upperdir=", StringComparison.Ordinal) || option.StartsWith("workdir=", StringComparison.Ordinal))
            .Select(option => OverlayUnescape(Unescape(option[(option.IndexOf('=', StringComparison.Ordinal) + 1)..])))];
    }

    // The mount path falls under: its device, root, mount point, and after the " - " separator its file system type and super options.
    private static (string Device, string Root, string MountPoint, string FileSystem, string SuperOptions) Holding(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        (string Device, string Root, string MountPoint, string FileSystem, string SuperOptions)? best = null;
        foreach (string line in File.ReadLines(MountInfo))
        {
            // id, parent, device, root, mount point, options, optional fields, "-", file system type, source, super options.
            string[] fields = line.Split(' ');
            int separator = fields.Length >= 10 ? Array.IndexOf(fields, "-", 6) : -1;
            if (separator < 0 || separator + 3 >= fields.Length)
            {
                continue;
            }

            string mountPoint = Unescape(fields[4]);

            // The last mount on a mount point hides the earlier ones, so a later line of equal length wins.
            if (IsSameOrUnder(path, mountPoint) && (best is null || mountPoint.Length >= best.Value.MountPoint.Length))
            {
                best = (fields[2], Unescape(fields[3]), mountPoint, fields[separator + 1], fields[separator + 3]);
            }
        }

        return best ?? throw new IOException($"No mount in {MountInfo} holds '{path}'.");
    }

    /// <summary>Whether <paramref name="path"/> is <paramref name="parent"/> or inside it, by path components.</summary>
    public static bool IsSameOrUnder(string path, string parent)
    {
        if (parent == "/")
        {
            return path.StartsWith('/');
        }

        string trimmed = parent.TrimEnd('/');
        return path == trimmed || path.StartsWith(trimmed + "/", StringComparison.Ordinal);
    }

    // overlayfs's own quoting of its directory options: a backslash stands for the character after it.
    private static string OverlayUnescape(string value)
    {
        if (!value.Contains('\\', StringComparison.Ordinal))
        {
            return value;
        }

        var text = new StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\' && i + 1 < value.Length)
            {
                i++;
            }

            text.Append(value[i]);
        }

        return text.ToString();
    }

    // mountinfo writes space, tab, newline and backslash as \ and three octal digits.
    private static string Unescape(string field)
    {
        if (!field.Contains('\\', StringComparison.Ordinal))
        {
            return field;
        }

        var text = new StringBuilder(field.Length);
        for (int i = 0; i < field.Length; i++)
        {
            if (field[i] == '\\' && i + 3 < field.Length && !field.AsSpan(i + 1, 3).ContainsAnyExceptInRange('0', '7'))
            {
                text.Append((char)Convert.ToInt32(field.Substring(i + 1, 3), 8));
                i += 3;
            }
            else
            {
                text.Append(field[i]);
            }
        }

        return text.ToString();
    }
}
