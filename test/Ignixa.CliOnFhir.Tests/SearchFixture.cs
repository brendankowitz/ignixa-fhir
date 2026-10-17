// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Search;
using Ignixa.CliOnFhir.Session;
using Ignixa.Specification.Generated;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>Small folders and helpers shared by the search and read tests.</summary>
internal static class SearchFixture
{
    public const string PatientsFile = "patients.ndjson";

    public const string ObservationsFile = "obs.ndjson";

    /// <summary>Three patients and five observations; p1 and p3 are Smiths, p2 is born in 1985, p3 in 1990.</summary>
    public static TestFolder Clinic() => new TestFolder()
        .Write(PatientsFile, """
            {"resourceType":"Patient","id":"p1","name":[{"family":"Smith","given":["Ann"]}],"birthDate":"1975-03-04","gender":"female"}
            {"resourceType":"Patient","id":"p2","name":[{"family":"Jones","given":["Bo"]}],"birthDate":"1985-06-07","gender":"male"}
            {"resourceType":"Patient","id":"p3","name":[{"family":"Smith","given":["Cy"]}],"birthDate":"1990-02-03","gender":"male"}
            """)
        .Write(ObservationsFile, """
            {"resourceType":"Observation","id":"o1","status":"final","code":{"coding":[{"system":"http://loinc.org","code":"8867-4"}]},"subject":{"reference":"Patient/p1"},"valueQuantity":{"value":60,"unit":"/min","system":"http://unitsofmeasure.org","code":"/min"}}
            {"resourceType":"Observation","id":"o2","status":"final","code":{"coding":[{"system":"http://loinc.org","code":"8867-4"}]},"subject":{"reference":"Patient/p1"},"valueQuantity":{"value":72,"unit":"/min","system":"http://unitsofmeasure.org","code":"/min"}}
            {"resourceType":"Observation","id":"o3","status":"final","code":{"coding":[{"system":"http://loinc.org","code":"8867-4"}]},"subject":{"reference":"Patient/p2"},"valueQuantity":{"value":80,"unit":"/min","system":"http://unitsofmeasure.org","code":"/min"}}
            {"resourceType":"Observation","id":"o4","status":"final","code":{"coding":[{"system":"http://loinc.org","code":"8480-6"}]},"subject":{"reference":"Patient/p2"},"valueQuantity":{"value":120,"unit":"mm[Hg]","system":"http://unitsofmeasure.org","code":"mm[Hg]"}}
            {"resourceType":"Observation","id":"o5","status":"final","code":{"coding":[{"system":"http://loinc.org","code":"8867-4"}]},"subject":{"reference":"Patient/p1"},"valueQuantity":{"value":90,"unit":"/min","system":"http://unitsofmeasure.org","code":"/min"}}
            """);

    public static FolderSession Open(TestFolder folder) => new(folder.Path, new R4CoreSchemaProvider(), NullLoggerFactory.Instance);

    /// <summary>The ids of the resources a search returned, in the order returned.</summary>
    public static string[] Ids(ToolEnvelope<SearchFacts?> envelope) =>
        [.. envelope.Facts.ShouldNotBeNull().Resources.ShouldNotBeNull().Select(resource => (string)resource["id"]!)];

    public static JsonNode Single(ToolEnvelope<SearchFacts?> envelope) => envelope.Facts.ShouldNotBeNull().Resources.ShouldNotBeNull().ShouldHaveSingleItem();
}
