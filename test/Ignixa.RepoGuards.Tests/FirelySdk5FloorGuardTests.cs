// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Xml.Linq;
using Shouldly;
using Xunit;

namespace Ignixa.RepoGuards.Tests;

/// <summary>
/// Guards the Firely SDK 5.x dependency floor. Ignixa.Extensions.FirelySdk5 exists to serve
/// consumers pinned to Firely 5.x - principally the Microsoft FHIR server, which runs 5.11.4 and
/// whose own characterization tests pin version-sensitive behaviour. A higher floor here would drag
/// that consumer onto a later 5.x with a known bug, so the shipped floor, the parity tests, and the
/// head-to-head benchmark must all name the same version.
/// </summary>
public class FirelySdk5FloorGuardTests
{
    private const string FirelySdk5Floor = "5.11.4";

    private static readonly string[] FirelyPackages =
    [
        "Hl7.Fhir.Base",
        "Hl7.Fhir.R4",
        "Hl7.Fhir.R4B",
        "Hl7.Fhir.R5",
        "Hl7.Fhir.Stu3",
    ];

    [Fact]
    public void GivenFirelySdk5Package_WhenReadingItsPins_ThenEveryFirelyPackageIsAtTheSupportedFloor()
    {
        var pins = ReadPackageVersions(Path.Combine(
            RepoRoot.Find(), "src", "Core", "Extensions", "Ignixa.Extensions.FirelySdk5", "Directory.Packages.props"));

        var declared = FirelyPackages
            .Select(package => new { package, version = Lookup(pins, package) })
            .ToList();

        declared.Where(entry => entry.version is null).Select(entry => entry.package)
            .ShouldBeEmpty("Ignixa.Extensions.FirelySdk5 must pin every Firely SDK package it ships against.");

        declared.Where(entry => entry.version != FirelySdk5Floor)
            .Select(entry => $"{entry.package}={entry.version}")
            .ShouldBeEmpty(
                $"Ignixa.Extensions.FirelySdk5 must ship against Firely {FirelySdk5Floor}, the version the " +
                "Microsoft FHIR server runs. Later 5.x releases carry a known bug, and raising this floor " +
                "forces that consumer's SDK up with it.");
    }

    [Fact]
    public void GivenFirelySeamMeasurements_WhenComparingToTheShippedFloor_ThenTheyMeasureTheVersionThatShips()
    {
        var repoRoot = RepoRoot.Find();

        var benchPins = ReadPackageVersions(Path.Combine(
            repoRoot, "bench", "Ignixa.Benchmarks.Firely5", "Directory.Packages.props"));
        var parityPins = ReadVersionOverrides(Path.Combine(
            repoRoot, "test", "Ignixa.FhirPath.Tests", "Ignixa.FhirPath.Tests.csproj"));

        var measured = benchPins.Concat(parityPins)
            .Where(pin => pin.Key.StartsWith("Hl7.Fhir.", StringComparison.OrdinalIgnoreCase))
            .ToList();

        measured.ShouldNotBeEmpty("Expected the benchmark and parity tests to pin Firely explicitly.");

        measured.Where(pin => pin.Value != FirelySdk5Floor)
            .Select(pin => $"{pin.Key}={pin.Value}")
            .ShouldBeEmpty(
                $"The Firely 5.x seam is measured at whatever these projects pin. They must stay at " +
                $"{FirelySdk5Floor} so the parity and benchmark numbers describe the engine " +
                "Ignixa.Extensions.FirelySdk5 actually ships against.");
    }

    private static string? Lookup(List<KeyValuePair<string, string>> pins, string package) =>
        pins.FirstOrDefault(pin => string.Equals(pin.Key, package, StringComparison.OrdinalIgnoreCase)).Value;

    private static List<KeyValuePair<string, string>> ReadPackageVersions(string propsPath)
    {
        File.Exists(propsPath).ShouldBeTrue($"Expected pins at {propsPath}; the guard's path is stale.");

        return XDocument.Load(propsPath)
            .Descendants("PackageVersion")
            .Select(element => new KeyValuePair<string, string>(
                (element.Attribute("Update") ?? element.Attribute("Include"))?.Value ?? string.Empty,
                element.Attribute("Version")?.Value ?? string.Empty))
            .Where(pin => pin.Key.Length > 0)
            .ToList();
    }

    private static List<KeyValuePair<string, string>> ReadVersionOverrides(string csprojPath)
    {
        File.Exists(csprojPath).ShouldBeTrue($"Expected a project at {csprojPath}; the guard's path is stale.");

        return XDocument.Load(csprojPath)
            .Descendants("PackageReference")
            .Where(element => element.Attribute("VersionOverride") is not null)
            .Select(element => new KeyValuePair<string, string>(
                element.Attribute("Include")?.Value ?? string.Empty,
                element.Attribute("VersionOverride")!.Value))
            .Where(pin => pin.Key.Length > 0)
            .ToList();
    }
}
