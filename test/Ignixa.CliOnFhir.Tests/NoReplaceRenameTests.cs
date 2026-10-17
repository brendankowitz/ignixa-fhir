// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Output;
using Shouldly;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>A rename into a free place never replaces a file that appears there, and on Linux is one atomic step.</summary>
public class NoReplaceRenameTests
{
    [Fact]
    public void GivenAFreeDestination_WhenMoved_ThenTheFileIsThere()
    {
        using TestFolder folder = new TestFolder().Write("a", "content");

        NoReplaceRename.Move(Path.Combine(folder.Path, "a"), Path.Combine(folder.Path, "b"));

        File.ReadAllText(Path.Combine(folder.Path, "b")).ShouldBe("content");
        File.Exists(Path.Combine(folder.Path, "a")).ShouldBeFalse();
    }

    [Fact]
    public void GivenAFileAtTheDestination_WhenMoved_ThenNothingIsReplaced()
    {
        using TestFolder folder = new TestFolder().Write("a", "new").Write("b", "existing");

        Should.Throw<IOException>(() => NoReplaceRename.Move(Path.Combine(folder.Path, "a"), Path.Combine(folder.Path, "b")));

        File.ReadAllText(Path.Combine(folder.Path, "b")).ShouldBe("existing");
        File.ReadAllText(Path.Combine(folder.Path, "a")).ShouldBe("new");
    }

    // .NET's own no-overwrite move is link() then unlink() on Linux; renameat2(RENAME_NOREPLACE) is the one atomic call.
    [Fact]
    public void GivenLinux_WhenARenameIsDone_ThenItUsedRenameat2()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using TestFolder folder = new TestFolder().Write("a", "x");

        NoReplaceRename.Move(Path.Combine(folder.Path, "a"), Path.Combine(folder.Path, "b"));

        NoReplaceRename.IsAtomic.ShouldBeTrue();
    }
}
