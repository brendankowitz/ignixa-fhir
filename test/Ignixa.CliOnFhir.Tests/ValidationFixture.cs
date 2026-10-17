// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Session;
using Ignixa.CliOnFhir.Terminology;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>
/// A hand-written package carrying R4's administrative-gender CodeSystem and ValueSet (4.0.1, as Patient.gender's required
/// binding names them) and a differential-only Patient profile requiring birthDate, served by a stub registry.
/// </summary>
internal static class ValidationFixture
{
    public const string PackageId = "example.validation";
    public const string PackageVersion = "1.0.0";
    public const string Package = $"{PackageId}@{PackageVersion}";
    public const string BirthRequired = "http://example.org/fhir/StructureDefinition/birth-required";
    public const string GenderValueSet = "http://hl7.org/fhir/ValueSet/administrative-gender|4.0.1";

    public static IReadOnlyList<(string Path, string Content)> Files() => TerminologyFixture.PackageFiles(
        PackageId,
        PackageVersion,
        "4.0.1",
        TerminologyFixture.CodeSystem("http://hl7.org/fhir/administrative-gender", "4.0.1", string.Empty, "male", "female", "other", "unknown"),
        """{"resourceType":"ValueSet","id":"administrative-gender","url":"http://hl7.org/fhir/ValueSet/administrative-gender","version":"4.0.1","compose":{"include":[{"system":"http://hl7.org/fhir/administrative-gender"}]}}""",
        $$$"""
        {"resourceType":"StructureDefinition","id":"birth-required","url":"{{{BirthRequired}}}","version":"1.0.0","name":"BirthRequired","status":"active","fhirVersion":"4.0.1",
         "kind":"resource","abstract":false,"type":"Patient","baseDefinition":"http://hl7.org/fhir/StructureDefinition/Patient","derivation":"constraint",
         "differential":{"element":[{"id":"Patient.birthDate","path":"Patient.birthDate","min":1}]}}
        """);

    /// <summary>A session whose terminology is the fixture package, loaded on first use (no default core package).</summary>
#pragma warning disable CA2000 // The registry client owns the stub handler and lives as long as the session, as in the tool.
    public static FolderSession WithTerminology(TestFolder folder, TestFolder store) =>
        TerminologyFixture.Open(folder, TerminologyFixture.Online(Registry(), store, Package));

    /// <summary>A session that can download the fixture package on request but configures no terminology package.</summary>
    public static FolderSession WithRegistry(TestFolder folder, TestFolder store) =>
        TerminologyFixture.Open(folder, TerminologyFixture.Online(Registry(), store) with { Packages = [] });
#pragma warning restore CA2000

    /// <summary>A session with no terminology at all: no packages, no store, no network.</summary>
    public static FolderSession WithoutTerminology(TestFolder folder) => TerminologyFixture.Open(folder, TerminologyOptions.None);

    private static StubHttpHandler Registry() => TerminologyFixture.Registry([(PackageId, PackageVersion, TerminologyFixture.Archive(Files()), null)]);
}
