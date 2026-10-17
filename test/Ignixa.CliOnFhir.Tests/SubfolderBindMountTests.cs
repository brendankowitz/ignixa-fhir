// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.DeId;
using Ignixa.CliOnFhir.Mapping;
using Ignixa.CliOnFhir.Views;
using Shouldly;
using static Ignixa.CliOnFhir.Tests.DeIdFixture;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>
/// A bind mount of a subfolder of the data folder is a path with a device and inode of its own: neither the path checks nor the
/// folder's identity see it. The mount table does, for every verb that writes.
/// </summary>
public class SubfolderBindMountTests
{
    [BindMountFact]
    public async Task GivenABindMountOfASubfolderOfTheData_WhenEachVerbWritesUnderIt_ThenEachIsRefusedAndTheFolderIsUnchanged()
    {
        using TestFolder root = Root();
        Directory.CreateDirectory(Path.Combine(Data(root), "nested"));
        string mount = Directory.CreateDirectory(Path.Combine(root.Path, "bind2")).FullName;
        TestProcess.TryRun("mount", "--bind", Path.Combine(Data(root), "nested"), mount).ShouldBeTrue();
        try
        {
            string[] before = Snapshot(Data(root));

            var view = await ViewFixture.RunInAsync(Data(root), new ViewRequest(ViewFixture.PatientView, Out: Path.Combine(mount, "rows.csv")));
            var deid = await RunAsync(Data(root), new DeIdRequest(Config, Path.Combine(mount, "deid")));
            var map = await MapFixture.RunAsync(Data(root), new MapRequest(MapFixture.PatientToBasic, Out: Path.Combine(mount, "map")));

            ViewFixture.Error(view).Code.ShouldBe("out-refused");
            Error(deid).Code.ShouldBe("out-refused");
            MapFixture.Error(map).Diagnostics.ShouldContain("a bind mount of a part of the folder");
            Snapshot(Data(root)).ShouldBe(before);
        }
        finally
        {
            TestProcess.TryRun("umount", mount).ShouldBeTrue();
        }
    }
}
