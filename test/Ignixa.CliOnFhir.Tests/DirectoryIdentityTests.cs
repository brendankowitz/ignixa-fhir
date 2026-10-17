// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Output;
using Shouldly;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>A directory by device and inode, which is what tells a bind mount of the data folder from another directory.</summary>
public class DirectoryIdentityTests
{
    [Fact]
    public void GivenAnotherSystemThanLinux_WhenAsked_ThenIdentitiesAreNotAvailable()
    {
        if (!OperatingSystem.IsLinux())
        {
            DirectoryIdentity.IsAvailable.ShouldBeFalse();
        }
    }

    [StatxFact]
    public void GivenADirectoryReachedThroughALink_WhenItsIdentityIsRead_ThenItIsTheDirectorysOwn()
    {

        using var root = new TestFolder();
        string data = Directory.CreateDirectory(Path.Combine(root.Path, "data")).FullName;
        string other = Directory.CreateDirectory(Path.Combine(root.Path, "other")).FullName;
        string alias = Path.Combine(root.Path, "alias");
        Directory.CreateSymbolicLink(alias, data);

        DirectoryIdentity.Of(alias).ShouldBe(DirectoryIdentity.Of(data));
        DirectoryIdentity.Of(other).ShouldNotBe(DirectoryIdentity.Of(data));
        DirectoryIdentity.Of(Path.Combine(root.Path, "missing")).ShouldBeNull();
    }

    [StatxFact]
    public void GivenAPathUnderAnAliasOfTheFolder_WhenItsAncestorsAreCompared_ThenTheAliasIsFoundAndNotJustTheExistingPart()
    {

        using var root = new TestFolder();
        string data = Directory.CreateDirectory(Path.Combine(root.Path, "data")).FullName;
        string other = Directory.CreateDirectory(Path.Combine(root.Path, "other")).FullName;
        string alias = Path.Combine(root.Path, "alias");
        Directory.CreateSymbolicLink(alias, data);
        DirectoryIdentity folder = DirectoryIdentity.Of(data).ShouldNotBeNull();

        DirectoryIdentity.SameAsOrUnder(Path.Combine(alias, "not", "made", "yet"), folder).ShouldBe(alias);
        DirectoryIdentity.SameAsOrUnder(Path.Combine(other, "not", "made"), folder).ShouldBeNull();
    }

    // Pins the statx buffer layout: the inode and device must be what stat(1) reports for the same directory.
    [StatxFact]
    public void GivenADirectory_WhenItsIdentityIsRead_ThenTheInodeAndDeviceAreThoseStatReports()
    {
        using var root = new TestFolder();
        if (TestProcess.Run("stat", "-c", "%i %Hd %Ld", root.Path) is not { ExitCode: 0 } stat)
        {
            return; // stat(1) is not installed; nothing to compare with
        }

        string[] parts = stat.Output.Trim().Split(' ');
        DirectoryIdentity identity = DirectoryIdentity.Of(root.Path).ShouldNotBeNull();

        identity.Inode.ShouldBe(ulong.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture));
        identity.DeviceMajor.ShouldBe(uint.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture));
        identity.DeviceMinor.ShouldBe(uint.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture));
    }
}
