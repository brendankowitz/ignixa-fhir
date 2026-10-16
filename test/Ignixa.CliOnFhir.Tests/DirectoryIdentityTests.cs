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
    public void GivenADirectoryReachedThroughALink_WhenItsIdentityIsRead_ThenItIsTheDirectorysOwn()
    {
        if (!OperatingSystem.IsLinux())
        {
            DirectoryIdentity.IsAvailable.ShouldBeFalse();
            return;
        }

        using var root = new TestFolder();
        string data = Directory.CreateDirectory(Path.Combine(root.Path, "data")).FullName;
        string other = Directory.CreateDirectory(Path.Combine(root.Path, "other")).FullName;
        string alias = Path.Combine(root.Path, "alias");
        Directory.CreateSymbolicLink(alias, data);

        DirectoryIdentity.Of(alias).ShouldBe(DirectoryIdentity.Of(data));
        DirectoryIdentity.Of(other).ShouldNotBe(DirectoryIdentity.Of(data));
        DirectoryIdentity.Of(Path.Combine(root.Path, "missing")).ShouldBeNull();
    }

    [Fact]
    public void GivenAPathUnderAnAliasOfTheFolder_WhenItsAncestorsAreCompared_ThenTheAliasIsFoundAndNotJustTheExistingPart()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var root = new TestFolder();
        string data = Directory.CreateDirectory(Path.Combine(root.Path, "data")).FullName;
        string other = Directory.CreateDirectory(Path.Combine(root.Path, "other")).FullName;
        string alias = Path.Combine(root.Path, "alias");
        Directory.CreateSymbolicLink(alias, data);
        DirectoryIdentity folder = DirectoryIdentity.Of(data).ShouldNotBeNull();

        DirectoryIdentity.SameAsOrUnder(Path.Combine(alias, "not", "made", "yet"), folder).ShouldBe(alias);
        DirectoryIdentity.SameAsOrUnder(Path.Combine(other, "not", "made"), folder).ShouldBeNull();
    }
}
