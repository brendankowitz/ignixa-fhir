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
/// A file written under an overlay mount lands in the overlay's upper directory, and the overlay keeps temporary files in its work
/// directory. With either in the data folder, the mount is a path into the folder that no path check, identity or device mapping of
/// the mount itself sees; the overlay's options in the mount table do.
/// </summary>
public class OverlayMountTests
{
    // The layers' names hold commas, which the mount table escapes twice (as \054, and overlayfs's own backslash).
    [OverlayMountFact]
    public async Task GivenAnOverlayWritingIntoTheData_WhenEachVerbWritesUnderIt_ThenEachIsRefusedAndTheFolderIsUnchanged()
    {
        using TestFolder root = Root();
        using OverlayMount overlay = OverlayMount.Create(root, Path.Combine(Data(root), "up,per"), Path.Combine(Data(root), "wo,rk"));
        string[] before = Snapshot(Data(root));

        var view = await ViewFixture.RunInAsync(Data(root), new ViewRequest(ViewFixture.PatientView, Out: Path.Combine(overlay.Merged, "rows.csv")));
        var deid = await RunAsync(Data(root), new DeIdRequest(Config, Path.Combine(overlay.Merged, "deid")));
        var map = await MapFixture.RunAsync(Data(root), new MapRequest(MapFixture.PatientToBasic, Out: Path.Combine(overlay.Merged, "map")));

        ViewFixture.Error(view).Code.ShouldBe("out-refused");
        Error(deid).Code.ShouldBe("out-refused");
        MapFixture.Error(map).Diagnostics.ShouldContain($"is on an overlay mount that writes into '{Path.Combine(Data(root), "up,per")}', which is in the data folder");
        Snapshot(Data(root)).ShouldBe(before);
    }

    [OverlayMountFact]
    public async Task GivenAnOverlayWhoseWorkDirectoryAloneIsInTheData_WhenDeIdentifyingUnderIt_ThenItIsRefused()
    {
        using TestFolder root = Root();
        using OverlayMount overlay = OverlayMount.Create(root, Path.Combine(root.Path, "upper"), Path.Combine(Data(root), "work"));

        Error(await RunAsync(Data(root), new DeIdRequest(Config, Path.Combine(overlay.Merged, "deid")))).Diagnostics.ShouldContain($"writes into '{Path.Combine(Data(root), "work")}'");
    }

    [OverlayMountFact]
    public async Task GivenAnOverlayWhoseLayersAreOutsideTheData_WhenDeIdentifyingUnderIt_ThenTheFilesAreWritten()
    {
        using TestFolder root = Root();
        using OverlayMount overlay = OverlayMount.Create(root, Path.Combine(root.Path, "upper"), Path.Combine(root.Path, "work"));

        DeIdFacts facts = Facts(await RunAsync(Data(root), new DeIdRequest(Config, Path.Combine(overlay.Merged, "deid"))));

        facts.Written.ShouldBeGreaterThan(0);
        File.Exists(Path.Combine(root.Path, "upper", "deid", "Patient.ndjson")).ShouldBeTrue();
    }
}
