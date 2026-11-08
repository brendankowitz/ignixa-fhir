// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.Search.Indexing;

namespace Ignixa.Search.Tests.Indexing;

/// <summary>
/// Regressions: the implicit system <see cref="CodeSystemResolver"/> gives a FHIR <c>code</c> element, read from the
/// generated mappings. Every row fails at bdb927b, where the generator guessed <c>http://hl7.org/fhir/CodeSystem/{name}</c>
/// or fell back to the bound ValueSet's own URL. Rows that pin unchanged behavior live in
/// <see cref="CodeSystemResolverGuardTests"/>.
/// </summary>
public class CodeSystemResolverTests
{
    [Theory]
    [InlineData(FhirVersion.R4, "Patient.gender", "http://hl7.org/fhir/administrative-gender")]
    [InlineData(FhirVersion.R4, "Patient.contact.gender", "http://hl7.org/fhir/administrative-gender")]
    [InlineData(FhirVersion.R4, "MedicationAdministration.status", "http://terminology.hl7.org/CodeSystem/medication-admin-status")]
    [InlineData(FhirVersion.R4, "Binary.contentType", "urn:ietf:bcp:13")]
    [InlineData(FhirVersion.R5, "Patient.language", "urn:ietf:bcp:47")]
    [InlineData(FhirVersion.R5, "Patient.gender", "http://hl7.org/fhir/administrative-gender")]
    [InlineData(FhirVersion.Stu3, "Patient.gender", "http://hl7.org/fhir/administrative-gender")]
    [InlineData(FhirVersion.R4B, "Observation.status", "http://hl7.org/fhir/observation-status")]
    [InlineData(FhirVersion.R6, "Patient.gender", "http://hl7.org/fhir/administrative-gender")]
    public void GivenACodeBoundToASingleSystemValueSet_WhenResolved_ThenItIsThatCodeSystemsCanonical(FhirVersion version, string path, string expected)
    {
        new CodeSystemResolver(version).ResolveSystem(path).ShouldBe(expected);
    }

    [Theory]
    [InlineData(FhirVersion.R4, "Encounter.class")]
    [InlineData(FhirVersion.R4, "Observation.code")]
    [InlineData(FhirVersion.R4, "Patient.communication.language")]
    [InlineData(FhirVersion.R4, "Task.intent")]
    [InlineData(FhirVersion.R5, "DeviceMetric.color")]
    [InlineData(FhirVersion.Stu3, "Binary.contentType")]
    public void GivenAnElementWithNoSingleImpliedSystem_WhenResolved_ThenNoSystemIsGiven(FhirVersion version, string path)
    {
        // Encounter.class is a Coding, Observation.code and Patient.communication.language CodeableConcepts: each
        // carries its system in the instance.
        // Task.intent and DeviceMetric.color are codes bound to ValueSets drawing on several code systems, and STU3's
        // Binary.contentType binds a URI that is not a ValueSet in the package, so no system is implied.
        new CodeSystemResolver(version).ResolveSystem(path).ShouldBeNull();
    }
}
