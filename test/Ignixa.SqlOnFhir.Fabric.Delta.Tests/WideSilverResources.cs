// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using System.Text.Json.Nodes;
using Ignixa.Serialization.SourceNodes;
using static Ignixa.SqlOnFhir.Fabric.Delta.Tests.WriterContracts;

namespace Ignixa.SqlOnFhir.Fabric.Delta.Tests;

/// <summary>
/// Invented Observation and Patient tables in the Silver layout (Reference structs carrying <c>msftSourceReference</c>,
/// <c>_string</c> companions, nested structs and lists), with resources that fill nearly every column, so a memory
/// measurement sees rows of realistic width rather than a few narrow strings. Nothing here is derived from a real
/// Healthcare data solutions package.
/// </summary>
internal static class WideSilverResources
{
    public static JsonObject ObservationSchema() => SchemaOf(
        Field("msftModifiedDatetime", "timestamp"),
        Field("id", "string", nullable: false),
        Field("resourceType", "string"),
        Field("msftSourceSystem", "string"),
        Field("msftFilePath", "string"),
        Field("meta", MetaType()),
        Field("meta_lastUpdated", "timestamp"),
        Field("identifier", ArrayOf(IdentifierType())),
        Field("identifier_string", "string"),
        Field("status", "string"),
        Field("category", ArrayOf(CodeableConceptType())),
        Field("category_string", "string"),
        Field("code", CodeableConceptType()),
        Field("code_string", "string"),
        Field("subject", ReferenceType()),
        Field("subject_string", "string"),
        Field("encounter", ReferenceType()),
        Field("encounter_string", "string"),
        Field("effectiveDateTime", "timestamp"),
        Field("issued", "timestamp"),
        Field("performer", ArrayOf(ReferenceType())),
        Field("performer_string", "string"),
        Field("valueQuantity", QuantityType()),
        Field("valueQuantity_string", "string"),
        Field("interpretation", ArrayOf(CodeableConceptType())),
        Field("interpretation_string", "string"),
        Field("note", ArrayOf(Struct(Field("authorString", "string"), Field("time", "timestamp"), Field("text", "string")))),
        Field("note_string", "string"),
        Field("referenceRange", ArrayOf(Struct(Field("low", QuantityType()), Field("high", QuantityType()), Field("text", "string")))),
        Field("referenceRange_string", "string"),
        Field("component", ArrayOf(Struct(
            Field("code", CodeableConceptType()),
            Field("valueQuantity", QuantityType()),
            Field("interpretation", ArrayOf(CodeableConceptType()))))),
        Field("component_string", "string"),
        Field("msftCreatedDatetime", "timestamp"));

    public static JsonObject PatientSchema() => SchemaOf(
        Field("msftModifiedDatetime", "timestamp"),
        Field("id", "string", nullable: false),
        Field("resourceType", "string"),
        Field("msftSourceSystem", "string"),
        Field("msftFilePath", "string"),
        Field("meta", MetaType()),
        Field("meta_lastUpdated", "timestamp"),
        Field("identifier", ArrayOf(IdentifierType())),
        Field("identifier_string", "string"),
        Field("active", "boolean"),
        Field("name", ArrayOf(Struct(
            Field("use", "string"),
            Field("text", "string"),
            Field("family", "string"),
            Field("given", ArrayOf("string")),
            Field("prefix", ArrayOf("string")),
            Field("suffix", ArrayOf("string")),
            Field("period", PeriodType())))),
        Field("name_string", "string"),
        Field("telecom", ArrayOf(Struct(
            Field("system", "string"),
            Field("value", "string"),
            Field("use", "string"),
            Field("rank", "integer"),
            Field("period", PeriodType())))),
        Field("telecom_string", "string"),
        Field("gender", "string"),
        Field("birthDate", "date"),
        Field("address", ArrayOf(Struct(
            Field("use", "string"),
            Field("type", "string"),
            Field("text", "string"),
            Field("line", ArrayOf("string")),
            Field("city", "string"),
            Field("district", "string"),
            Field("state", "string"),
            Field("postalCode", "string"),
            Field("country", "string"),
            Field("period", PeriodType())))),
        Field("address_string", "string"),
        Field("maritalStatus", CodeableConceptType()),
        Field("maritalStatus_string", "string"),
        Field("communication", ArrayOf(Struct(Field("language", CodeableConceptType()), Field("preferred", "boolean")))),
        Field("communication_string", "string"),
        Field("generalPractitioner", ArrayOf(ReferenceType())),
        Field("generalPractitioner_string", "string"),
        Field("managingOrganization", ReferenceType()),
        Field("managingOrganization_string", "string"),
        Field("msftCreatedDatetime", "timestamp"));

    /// <summary>An Observation with local id <c>obs-{key}</c>, varied by <paramref name="key"/> so columns do not collapse to one value.</summary>
    public static ResourceJsonNode Observation(int key, string lastUpdated)
    {
        var value = 40 + (key % 900 / 10m);
        var resource = new JsonObject
        {
            ["resourceType"] = "Observation",
            ["id"] = Local("obs", key),
            ["meta"] = Meta(lastUpdated, "http://example.org/fhir/StructureDefinition/vital-signs"),
            ["identifier"] = new JsonArray(
                IdentifierOf("http://example.org/lab/accession", $"ACC-{key:D8}", "ACSN", "Accession ID"),
                IdentifierOf("urn:ietf:rfc:3986", $"urn:uuid:{UuidOf(key)}", "RI", "Resource identifier")),
            ["status"] = key % 7 == 0 ? "amended" : "final",
            ["category"] = new JsonArray(Concept(
                "Vital Signs",
                CodingOf("http://terminology.hl7.org/CodeSystem/observation-category", "vital-signs", "Vital Signs"))),
            ["code"] = Concept(
                "Systolic blood pressure",
                CodingOf("http://loinc.org", "8480-6", "Systolic blood pressure"),
                CodingOf("http://snomed.info/sct", "271649006", "Systolic blood pressure (observable entity)"),
                CodingOf("http://example.org/local-codes", $"SBP-{key % 50}", $"Local systolic code {key % 50}")),
            ["subject"] = ReferenceTo("Patient", Local("pat", key % 5_000), $"Patient {key % 5_000}"),
            ["encounter"] = ReferenceTo("Encounter", Local("enc", key % 20_000), $"Encounter {key % 20_000}"),
            ["effectiveDateTime"] = Instant(key, minutes: 0),
            ["issued"] = Instant(key, minutes: 42),
            ["performer"] = new JsonArray(
                ReferenceTo("Practitioner", Local("prac", key % 300), $"Dr. Practitioner {key % 300}"),
                ReferenceTo("Organization", Local("org", key % 40), $"Clinic {key % 40}")),
            ["valueQuantity"] = QuantityOf(value, "mm[Hg]"),
            ["interpretation"] = new JsonArray(Concept(
                value > 120 ? "High" : "Normal",
                CodingOf("http://terminology.hl7.org/CodeSystem/v3-ObservationInterpretation", value > 120 ? "H" : "N", value > 120 ? "High" : "Normal"))),
            ["note"] = new JsonArray(
                new JsonObject
                {
                    ["authorString"] = $"Nurse {key % 120}",
                    ["time"] = Instant(key, minutes: 5),
                    ["text"] = $"Patient seated for five minutes before measurement {key}; left arm, adult cuff, no distress reported.",
                },
                new JsonObject
                {
                    ["authorString"] = $"Reviewer {key % 30}",
                    ["time"] = Instant(key, minutes: 50),
                    ["text"] = $"Reviewed against the previous reading; follow-up scheduled per protocol {key % 11}.",
                }),
            ["referenceRange"] = new JsonArray(new JsonObject
            {
                ["low"] = QuantityOf(90, "mm[Hg]"),
                ["high"] = QuantityOf(120, "mm[Hg]"),
                ["text"] = "Adult normal range, seated",
            }),
            ["component"] = new JsonArray(
                Component("8480-6", "Systolic blood pressure", value),
                Component("8462-4", "Diastolic blood pressure", value * 0.65m),
                Component("8867-4", "Heart rate", 55 + (key % 45), "/min")),
        };

        return ResourceJsonNode.Parse(resource.ToJsonString());
    }

    /// <summary>A Patient with local id <c>pat-{key}</c>, varied by <paramref name="key"/>.</summary>
    public static ResourceJsonNode Patient(int key, string lastUpdated)
    {
        var resource = new JsonObject
        {
            ["resourceType"] = "Patient",
            ["id"] = Local("pat", key),
            ["meta"] = Meta(lastUpdated, "http://example.org/fhir/StructureDefinition/patient"),
            ["identifier"] = new JsonArray(
                IdentifierOf("http://example.org/mrn", $"MRN{key:D9}", "MR", "Medical record number"),
                IdentifierOf("http://example.org/national-id", $"{key % 900:D3}-{key % 90:D2}-{key % 9000:D4}", "SB", "Social beneficiary identifier"),
                IdentifierOf("http://example.org/driver-licence", $"S{key:D8}", "DL", "Driver's license number")),
            ["active"] = key % 13 != 0,
            ["name"] = new JsonArray(
                new JsonObject
                {
                    ["use"] = "official",
                    ["text"] = $"Mx. Given{key % 700} Middle{key % 50} Family{key % 3_000}",
                    ["family"] = $"Family{key % 3_000}",
                    ["given"] = new JsonArray($"Given{key % 700}", $"Middle{key % 50}"),
                    ["prefix"] = new JsonArray("Mx."),
                    ["period"] = new JsonObject { ["start"] = "2001-02-03T04:05:06Z" },
                },
                new JsonObject
                {
                    ["use"] = "maiden",
                    ["family"] = $"Birthname{key % 2_000}",
                    ["given"] = new JsonArray($"Given{key % 700}"),
                    ["period"] = new JsonObject { ["start"] = "1980-01-01T00:00:00Z", ["end"] = "2001-02-03T04:05:06Z" },
                }),
            ["telecom"] = new JsonArray(
                Telecom("phone", $"+1-555-{key % 1000:D3}-{key % 10_000:D4}", "home", 1),
                Telecom("phone", $"+1-555-{(key + 17) % 1000:D3}-{(key + 31) % 10_000:D4}", "mobile", 2),
                Telecom("email", $"person{key}@example.org", "work", 3)),
            ["gender"] = (key % 3) switch { 0 => "female", 1 => "male", _ => "other" },
            ["birthDate"] = new DateOnly(1940, 1, 1).AddDays(key % 25_000).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["address"] = new JsonArray(
                Address("home", key, "Main Street"),
                Address("work", key + 1, "Commerce Avenue")),
            ["maritalStatus"] = Concept(
                "Married",
                CodingOf("http://terminology.hl7.org/CodeSystem/v3-MaritalStatus", "M", "Married")),
            ["communication"] = new JsonArray(
                new JsonObject
                {
                    ["language"] = Concept("English", CodingOf("urn:ietf:bcp:47", "en-US", "English (United States)")),
                    ["preferred"] = true,
                },
                new JsonObject
                {
                    ["language"] = Concept("Spanish", CodingOf("urn:ietf:bcp:47", "es", "Spanish")),
                    ["preferred"] = false,
                }),
            ["generalPractitioner"] = new JsonArray(ReferenceTo("Practitioner", Local("prac", key % 300), $"Dr. Practitioner {key % 300}")),
            ["managingOrganization"] = ReferenceTo("Organization", Local("org", key % 40), $"Clinic {key % 40}"),
        };

        return ResourceJsonNode.Parse(resource.ToJsonString());
    }

    private static string Local(string prefix, int key) => $"{prefix}-{key}";

    private static string UuidOf(int key) => $"00000000-0000-4000-8000-{key:D12}";

    private static string Instant(int key, int minutes) =>
        new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero).AddMinutes((key * 17L % 500_000) + minutes)
            .ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    private static JsonObject Meta(string lastUpdated, string profile) => new()
    {
        ["versionId"] = "3",
        ["lastUpdated"] = lastUpdated,
        ["source"] = "#example-source-system",
        ["profile"] = new JsonArray(profile),
    };

    private static JsonObject CodingOf(string system, string code, string display) => new()
    {
        ["system"] = system,
        ["code"] = code,
        ["display"] = display,
    };

    private static JsonObject Concept(string text, params JsonObject[] codings) => new()
    {
        ["coding"] = new JsonArray(codings),
        ["text"] = text,
    };

    private static JsonObject IdentifierOf(string system, string value, string typeCode, string typeText) => new()
    {
        ["use"] = "official",
        ["type"] = Concept(typeText, CodingOf("http://terminology.hl7.org/CodeSystem/v2-0203", typeCode, typeText)),
        ["system"] = system,
        ["value"] = value,
        ["period"] = new JsonObject { ["start"] = "2020-01-01T00:00:00Z" },
    };

    private static JsonObject ReferenceTo(string type, string id, string display) => new()
    {
        ["reference"] = $"{type}/{id}",
        ["type"] = type,
        ["display"] = display,
    };

    private static JsonObject QuantityOf(decimal value, string unit) => new()
    {
        ["value"] = value,
        ["unit"] = unit,
        ["system"] = "http://unitsofmeasure.org",
        ["code"] = unit,
    };

    private static JsonObject Component(string loinc, string display, decimal value, string unit = "mm[Hg]") => new()
    {
        ["code"] = Concept(display, CodingOf("http://loinc.org", loinc, display)),
        ["valueQuantity"] = QuantityOf(value, unit),
        ["interpretation"] = new JsonArray(Concept(
            "Normal",
            CodingOf("http://terminology.hl7.org/CodeSystem/v3-ObservationInterpretation", "N", "Normal"))),
    };

    private static JsonObject Telecom(string system, string value, string use, int rank) => new()
    {
        ["system"] = system,
        ["value"] = value,
        ["use"] = use,
        ["rank"] = rank,
        ["period"] = new JsonObject { ["start"] = "2015-06-01T00:00:00Z" },
    };

    private static JsonObject Address(string use, int key, string street) => new()
    {
        ["use"] = use,
        ["type"] = "both",
        ["text"] = $"{key % 9_000} {street}, Unit {key % 40}, City{key % 400}, ST {key % 90_000:D5}",
        ["line"] = new JsonArray($"{key % 9_000} {street}", $"Unit {key % 40}"),
        ["city"] = $"City{key % 400}",
        ["district"] = $"County{key % 60}",
        ["state"] = $"S{key % 50:D2}",
        ["postalCode"] = $"{key % 90_000:D5}",
        ["country"] = "US",
        ["period"] = new JsonObject { ["start"] = "2010-01-01T00:00:00Z" },
    };

    private static JsonObject MetaType() => Struct(
        Field("versionId", "string"),
        Field("lastUpdated", "timestamp"),
        Field("source", "string"),
        Field("profile", ArrayOf("string")));

    private static JsonObject CodingType() => Struct(
        Field("extension", "string"),
        Field("id", "string"),
        Field("system", "string"),
        Field("version", "string"),
        Field("code", "string"),
        Field("display", "string"),
        Field("userSelected", "boolean"));

    private static JsonObject CodeableConceptType() => Struct(
        Field("extension", "string"),
        Field("id", "string"),
        Field("coding", ArrayOf(CodingType())),
        Field("text", "string"));

    private static JsonObject PeriodType() => Struct(
        Field("extension", "string"),
        Field("id", "string"),
        Field("start", "timestamp"),
        Field("end", "timestamp"));

    private static JsonObject QuantityType() => Struct(
        Field("extension", "string"),
        Field("id", "string"),
        Field("value", "decimal(18,6)"),
        Field("comparator", "string"),
        Field("unit", "string"),
        Field("system", "string"),
        Field("code", "string"));

    private static JsonObject ShallowReferenceType() => Struct(
        Field("extension", "string"),
        Field("id", "string"),
        Field("reference", "string"),
        Field("type", "string"),
        Field("identifier", Struct(
            Field("extension", "string"),
            Field("use", "string"),
            Field("type", "string"),
            Field("value", "string"),
            Field("system", "string"),
            Field("period", "string"),
            Field("assigner", "string"))),
        Field("display", "string"));

    private static JsonObject IdentifierType() => Struct(
        Field("extension", "string"),
        Field("use", "string"),
        Field("value", "string"),
        Field("system", "string"),
        Field("type", CodeableConceptType()),
        Field("period", PeriodType()),
        Field("assigner", ShallowReferenceType()));

    private static JsonObject ReferenceType() => Struct(
        Field("reference", "string"),
        Field("type", "string"),
        Field("identifier", IdentifierType()),
        Field("display", "string"),
        Field("extension", "string"),
        Field("id", "string"),
        Field("idOrig", "string"),
        Field("msftSourceReference", "string"));
}
