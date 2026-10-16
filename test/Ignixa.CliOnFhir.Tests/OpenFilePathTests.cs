// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Caching;
using Ignixa.CliOnFhir.Views;
using Microsoft.Win32.SafeHandles;
using Shouldly;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>Where the operating system says an open file is, which is what the view output checks instead of a path string.</summary>
public class OpenFilePathTests
{
    [Fact]
    public void GivenAFileOpenedThroughALinkedDirectory_WhenAskedWhereItIs_ThenItIsWhereTheLinkLeads()
    {
        if (!OperatingSystem.IsLinux())
        {
            return; // symbolic links need a privilege on Windows; macOS has no handle lookup
        }

        using var root = new TestFolder();
        string real = Directory.CreateDirectory(Path.Combine(root.Path, "real")).FullName;
        string link = Path.Combine(root.Path, "link");
        Directory.CreateSymbolicLink(link, real);
        using SafeFileHandle handle = File.OpenHandle(Path.Combine(link, "a.tmp"), FileMode.CreateNew, FileAccess.ReadWrite);

        OpenFilePath.Of(handle).ShouldBe(Path.Combine(CanonicalPath.Resolve(real), "a.tmp"));
    }

    [Fact]
    public void GivenAnOpenFileWhoseDirectoryIsRenamed_WhenAskedWhereItIs_ThenItIsTheNewPlace()
    {
        if (!OperatingSystem.IsLinux())
        {
            return; // Windows does not rename a directory with an open file in it
        }

        using var root = new TestFolder();
        string before = Directory.CreateDirectory(Path.Combine(root.Path, "before")).FullName;
        using SafeFileHandle handle = File.OpenHandle(Path.Combine(before, "a.tmp"), FileMode.CreateNew, FileAccess.ReadWrite);
        string after = Path.Combine(root.Path, "after");

        Directory.Move(before, after);

        OpenFilePath.Of(handle).ShouldBe(Path.Combine(CanonicalPath.Resolve(after), "a.tmp"));
    }

    [Fact]
    public void GivenAnOpenFileThatWasDeleted_WhenAskedWhereItIs_ThenItHasNoPlace()
    {
        if (!OperatingSystem.IsLinux())
        {
            return; // Windows does not delete a file this process holds open without delete sharing
        }

        using var root = new TestFolder();
        string path = Path.Combine(root.Path, "a.tmp");
        using SafeFileHandle handle = File.OpenHandle(path, FileMode.CreateNew, FileAccess.ReadWrite);

        File.Delete(path);

        OpenFilePath.Of(handle).ShouldBeNull();
    }

    [Fact]
    public void GivenAnOpenFileOnWindows_WhenAskedWhereItIs_ThenItIsADrivePathToThatFile()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var root = new TestFolder();
        using SafeFileHandle handle = File.OpenHandle(Path.Combine(root.Path, "a.tmp"), FileMode.CreateNew, FileAccess.ReadWrite);

        string location = OpenFilePath.Of(handle).ShouldNotBeNull();

        location.ShouldNotStartWith(@"\\?\");
        Path.GetFileName(location).ShouldBe("a.tmp");
        File.Exists(location).ShouldBeTrue();
    }

    [Fact]
    public void GivenASystemWithoutAHandleLookup_WhenAsked_ThenItSaysSoRatherThanGuessing()
    {
        if (OperatingSystem.IsLinux() || OperatingSystem.IsWindows())
        {
            OpenFilePath.IsAvailable.ShouldBeTrue();
            return;
        }

        using var root = new TestFolder();
        using SafeFileHandle handle = File.OpenHandle(Path.Combine(root.Path, "a.tmp"), FileMode.CreateNew, FileAccess.ReadWrite);

        OpenFilePath.IsAvailable.ShouldBeFalse();
        Should.Throw<PlatformNotSupportedException>(() => OpenFilePath.Of(handle));
    }
}
