// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.CliOnFhir.Session;
using Ignixa.CliOnFhir.Validation;
using Shouldly;
using static Ignixa.CliOnFhir.Tests.ValidationFixture;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>
/// Task 9 fix round 4: a differential-only profile deriving from a profile with a core canonical (vitalsigns, loaded from a
/// package) inherits that profile's rules. The regression fails at 406a06e: there the walk up <c>baseDefinition</c> stopped
/// at any <c>http://hl7.org/fhir/StructureDefinition/</c> canonical, loaded or not, so the child applied no rule of its own.
/// </summary>
public class ValidateInheritedCoreProfileTests
{
    private const string VitalsPackageId = "example.vitals";
    private const string VitalsPackageVersion = "1.0.0";
    private const string VitalsPackage = $"{VitalsPackageId}@{VitalsPackageVersion}";
    private const string VitalSigns = "http://hl7.org/fhir/StructureDefinition/vitalsigns";
    private const string VsChild = "http://example.org/fhir/StructureDefinition/vs-child";

    // Regression: at 406a06e vc2 (a Group subject under vs-child, an empty differential over the package's vitalsigns) was valid.
    [Fact]
    public async Task GivenAnEmptyDifferentialOverALoadedCoreCanonicalProfile_WhenASubjectItDoesNotAllowIsValidated_ThenTheInheritedTargetIsChecked()
    {
        using var folder = new TestFolder();
        folder.Write("sd-vs-child.json", $$$"""
            {"resourceType":"StructureDefinition","id":"vs-child","url":"{{{VsChild}}}","version":"1.0.0","name":"VsChild","status":"active","fhirVersion":"4.0.1",
             "kind":"resource","abstract":false,"type":"Observation","baseDefinition":"{{{VitalSigns}}}","derivation":"constraint","differential":{"element":[]}}
            """);
        folder.Write("r.ndjson", $$$"""
            {"resourceType":"Observation","id":"vc1","meta":{"profile":["{{{VsChild}}}"]},"status":"final","code":{"text":"heart rate"},"subject":{"reference":"Patient/p1"}}
            {"resourceType":"Observation","id":"vc2","meta":{"profile":["{{{VsChild}}}"]},"status":"final","code":{"text":"heart rate"},"subject":{"reference":"Group/g1"}}
            """);
        using var store = new TestFolder();
        FolderSession session = WithVitalsPackage(folder, store);

        JsonNode result = TerminologyFixture.Json(await ResourceValidator.ValidateAsync(session, new ValidateRequest(ResourceType: "Observation", Depth: "full", Packages: [VitalsPackage])));

        ((int)result["facts"]!["verdicts"]!["invalid"]!).ShouldBe(1);
        ((int)result["facts"]!["verdicts"]!["valid"]!).ShouldBe(1);
        JsonNode group = result["facts"]!["groups"]!.AsArray().Single(item => (string)item!["rule"]! == "reference-target-type")!;
        ((string)group["profile"]!).ShouldBe(VsChild);
        ((string)group["path"]!).ShouldBe("Observation.subject");
        group["samples"]!.AsArray().Select(item => (string)item!).ShouldBe(["Observation/vc2"]);
    }

    /// <summary>A session whose stub registry serves the validation fixture package and a package carrying a vitalsigns-like profile.</summary>
#pragma warning disable CA2000 // The registry client owns the stub handler and lives as long as the session, as in the tool.
    private static FolderSession WithVitalsPackage(TestFolder folder, TestFolder store) =>
        TerminologyFixture.Open(folder, TerminologyFixture.Online(TerminologyFixture.Registry([(PackageId, PackageVersion, TerminologyFixture.Archive(Files()), null), (VitalsPackageId, VitalsPackageVersion, TerminologyFixture.Archive(VitalsFiles()), null)]), store, Package));
#pragma warning restore CA2000

    // Like the core vitalsigns profile in what matters here: a differential over Observation narrowing subject to Patient. The
    // package also carries observation-status, so a well-formed Observation has no unchecked binding and is valid.
    private static IReadOnlyList<(string Path, string Content)> VitalsFiles() => TerminologyFixture.PackageFiles(
        VitalsPackageId,
        VitalsPackageVersion,
        "4.0.1",
        TerminologyFixture.CodeSystem("http://hl7.org/fhir/observation-status", "4.0.1", string.Empty, "registered", "preliminary", "final", "amended", "corrected", "cancelled", "entered-in-error", "unknown"),
        """{"resourceType":"ValueSet","id":"observation-status","url":"http://hl7.org/fhir/ValueSet/observation-status","version":"4.0.1","compose":{"include":[{"system":"http://hl7.org/fhir/observation-status"}]}}""",
        $$$"""
        {"resourceType":"StructureDefinition","id":"vitalsigns","url":"{{{VitalSigns}}}","version":"4.0.1","name":"observation-vitalsigns","status":"active","fhirVersion":"4.0.1",
         "kind":"resource","abstract":false,"type":"Observation","baseDefinition":"http://hl7.org/fhir/StructureDefinition/Observation","derivation":"constraint",
         "differential":{"element":[{"id":"Observation.subject","path":"Observation.subject","min":1,"type":[{"code":"Reference","targetProfile":["http://hl7.org/fhir/StructureDefinition/Patient"]}]}]}}
        """);
}
