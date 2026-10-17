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
/// under this mapping it is a path under the folder's own path on the same device. Elsewhere it is not <see cref="IsAvailable"/>.
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
        ArgumentException.ThrowIfNullOrEmpty(path);

        (string Device, string Root, string MountPoint)? best = null;
        foreach (string line in File.ReadLines(MountInfo))
        {
            string[] fields = line.Split(' ');
            if (fields.Length < 5)
            {
                continue;
            }

            string mountPoint = Unescape(fields[4]);

            // The last mount on a mount point hides the earlier ones, so a later line of equal length wins.
            if (IsSameOrUnder(path, mountPoint) && (best is null || mountPoint.Length >= best.Value.MountPoint.Length))
            {
                best = (fields[2], Unescape(fields[3]), mountPoint);
            }
        }

        if (best is not { } mount)
        {
            throw new IOException($"No mount in {MountInfo} holds '{path}'.");
        }

        string rest = mount.MountPoint == "/" ? path : path[mount.MountPoint.Length..];
        string underlying = mount.Root == "/" ? (rest.Length == 0 ? "/" : rest) : mount.Root + rest;
        return (mount.Device, underlying);
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
