// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Patients;

/// <summary>The root digest of one patient's context: one stub per domain.</summary>
/// <param name="Patient">The patient, <c>Patient/id</c>.</param>
/// <param name="FhirVersion">The version the folder was read as, which the domain rules depend on.</param>
/// <param name="AsOf">The instant time-dependent domains (encounters, labs) were read at.</param>
/// <param name="CompartmentSize">The Patient and every resource in its compartment.</param>
/// <param name="Demographics">From the Patient resource.</param>
/// <param name="Problems">Active Conditions.</param>
/// <param name="Medications">Active MedicationRequests, then active MedicationStatements.</param>
/// <param name="Allergies">Every AllergyIntolerance, whatever its status.</param>
/// <param name="Encounters">Encounters in the window, newest first.</param>
/// <param name="Labs">The most frequent lab and vital-sign codes, with latest value and trend.</param>
/// <param name="OtherTypes">Compartment resources of types no stub covers, counted by type (search them with the compartment argument).</param>
/// <param name="ValueBindings">
/// <c>checked</c> when the session's terminology was loaded, so shown statuses, genders and comparators were also checked against
/// their required-binding ValueSets (a value outside is a digest, with an <c>invalid-value</c> issue naming the element and the
/// ValueSet); <c>not-checked</c> when no terminology call has loaded it, so only their shape was checked.
/// </param>
public sealed record RootDigestFacts(
    string Patient,
    string FhirVersion,
    DateTimeOffset AsOf,
    int CompartmentSize,
    Demographics Demographics,
    DomainStub Problems,
    DomainStub Medications,
    DomainStub Allergies,
    DomainStub Encounters,
    DomainStub Labs,
    IReadOnlyDictionary<string, int> OtherTypes,
    string ValueBindings);
