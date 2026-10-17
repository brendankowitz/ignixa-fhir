// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Tests;

/// <summary>A test that bind-mounts a directory: skipped, not passed, where mounting is not permitted (or not Linux).</summary>
internal sealed class BindMountFactAttribute : FactAttribute
{
    private static readonly Lazy<string?> Unavailable = new(Probe);

    public BindMountFactAttribute()
    {
        if (Unavailable.Value is { } reason)
        {
            Skip = reason;
        }
    }

    // A mount of one empty directory onto another, undone at once.
    private static string? Probe()
    {
        if (!OperatingSystem.IsLinux())
        {
            return "Bind mounts are tested on Linux only.";
        }

        using var root = new TestFolder();
        string source = Directory.CreateDirectory(Path.Combine(root.Path, "source")).FullName;
        string target = Directory.CreateDirectory(Path.Combine(root.Path, "target")).FullName;
        if (!TestProcess.TryRun("mount", "--bind", source, target))
        {
            return "Mounting is not permitted in this environment (mount --bind failed).";
        }

        TestProcess.TryRun("umount", target);
        return null;
    }
}
