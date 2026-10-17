// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Tests;

/// <summary>A test that mounts an overlay: skipped, not passed, where overlay mounts are not permitted (or not Linux).</summary>
internal sealed class OverlayMountFactAttribute : FactAttribute
{
    private static readonly Lazy<string?> Unavailable = new(Probe);

    public OverlayMountFactAttribute()
    {
        if (Unavailable.Value is { } reason)
        {
            Skip = reason;
        }
    }

    // An overlay of empty directories, undone at once.
    private static string? Probe()
    {
        if (!OperatingSystem.IsLinux())
        {
            return "Overlay mounts are tested on Linux only.";
        }

        using var root = new TestFolder();
        string lower = Directory.CreateDirectory(Path.Combine(root.Path, "lower")).FullName;
        string upper = Directory.CreateDirectory(Path.Combine(root.Path, "upper")).FullName;
        string work = Directory.CreateDirectory(Path.Combine(root.Path, "work")).FullName;
        string merged = Directory.CreateDirectory(Path.Combine(root.Path, "merged")).FullName;
        if (!TestProcess.TryRun("mount", "-t", "overlay", "overlay", "-o", $"lowerdir={lower},upperdir={upper},workdir={work}", merged))
        {
            return "Overlay mounts are not permitted in this environment (mount -t overlay failed).";
        }

        TestProcess.TryRun("umount", merged);
        return null;
    }
}
