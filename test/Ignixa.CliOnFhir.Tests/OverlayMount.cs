// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>An overlay mount for a test, its upper and work directories as given, unmounted when disposed.</summary>
internal sealed class OverlayMount : IDisposable
{
    private OverlayMount(string merged) => Merged = merged;

    public string Merged { get; }

    // overlayfs takes a backslash to quote a comma in a directory option.
    public static OverlayMount Create(TestFolder root, string upper, string work)
    {
        string lower = Directory.CreateDirectory(Path.Combine(root.Path, "lower")).FullName;
        string merged = Directory.CreateDirectory(Path.Combine(root.Path, "merged")).FullName;
        Directory.CreateDirectory(upper);
        Directory.CreateDirectory(work);
        string options = $"lowerdir={lower},upperdir={upper.Replace(",", "\\,", StringComparison.Ordinal)},workdir={work.Replace(",", "\\,", StringComparison.Ordinal)}";
        TestProcess.TryRun("mount", "-t", "overlay", "overlay", "-o", options, merged).ShouldBeTrue();
        return new OverlayMount(merged);
    }

    public void Dispose() => TestProcess.TryRun("umount", Merged).ShouldBeTrue();
}
